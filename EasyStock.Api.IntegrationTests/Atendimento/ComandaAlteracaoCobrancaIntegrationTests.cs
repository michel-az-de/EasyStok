using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;
using EasyStock.Domain.Entities.Pagamentos;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>#1971: restos da frente A na alteração da comanda (cobrança depois do commit, sinal, link substituído, cupom).</summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    // #1725-6: a alteração aceita chamava o Mercado Pago com o link da comanda travado (FOR UPDATE) até o provedor
    // responder; a página do cliente e o atendimento esperavam junto. O provedor fica parado até outra transação travar o link.
    [SkippableFact]
    public async Task Alteracao_LinkNaoFicaTravadoDuranteAChamadaAoProvedor()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var chamado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamadas = 0;
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(async c =>
        {
            var command = c.Arg<CriarPreferenceCommand>();
            if (Interlocked.Increment(ref chamadas) > 1)
            {
                chamado.TrySetResult();
                await liberar.Task;
            }
            return new PreferenceCriadaResult(command.IdempotencyKey!, $"https://www.mercadopago.com.br/checkout/{command.IdempotencyKey}");
        });
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var (ajuste, linkId) = await AlteracaoComAcrescimoAsync(factory, client, c);

        var envio = client.PostAsJsonAsync($"{Rota(ajuste)}/confirmar", new { revisao = 1 });
        (await Task.WhenAny(chamado.Task, envio, Task.Delay(TimeSpan.FromSeconds(30)))).Should().Be(chamado.Task,
            "a alteração aceita sozinha chega ao provedor");

        try
        {
            await using var conexao = new NpgsqlConnection(_pg!.GetConnectionString());
            await conexao.OpenAsync();
            await using var tx = await conexao.BeginTransactionAsync();
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '2s'", conexao, tx))
                await timeout.ExecuteNonQueryAsync();
            await using var travar = new NpgsqlCommand(
                "SELECT \"ResultadoJson\" FROM links_cardapio_conversa WHERE \"Id\" = @id FOR UPDATE", conexao, tx);
            travar.Parameters.AddWithValue("id", linkId);
            var resultado = (string?)await travar.ExecuteScalarAsync();
            resultado.Should().NotBeNull("a alteração foi gravada e commitada antes da cobrança");
            await tx.CommitAsync();
        }
        finally
        {
            liberar.TrySetResult();
        }

        var resposta = await envio;
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var corpo = (await resposta.Content.ReadFromJsonAsync<EnvioComandaCliente>())!;
        corpo.Situacao.Should().Be("aguardando_pagamento");
        corpo.LinkPagamento.Should().NotBeNull();
        (await Estado(client, ajuste)).Envio!.LinkPagamento.Should().Be(corpo.LinkPagamento, "o resultado final fica no link");
    }

    // #1717-7 e #1717-8: na encomenda sem nada pago, a alteração cobrava o total (o console cobra o sinal) e o link do
    // sinal antigo, cancelado no banco, seguia pagável no Mercado Pago.
    [SkippableFact]
    public async Task Alteracao_EncomendaSemSinalPagoCobraOSinalEExpiraOLinkAntigo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        var mp = Provedor(preferencias);
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var (ajuste, _) = await AlteracaoComAcrescimoAsync(factory, client, c);
        var antiga = preferencias.Keys.Single();

        var envio = await Enviar(client, ajuste, 1);

        envio.Situacao.Should().Be("aguardando_pagamento");
        envio.Total.Should().Be(25m);
        preferencias.Should().HaveCount(2);
        preferencias.Values.Single(p => p.IdempotencyKey != antiga).ValorTotal.Should().Be(12.5m,
            "encomenda sem nada pago cobra o sinal de 50%, a mesma conta do console");
        await mp.Received(1).ExpirarPreferenciaAsync(antiga, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // #1725-6: com a cobrança depois do commit, o provedor fora do ar não pode deixar a alteração aplicada sem cobrança
    // (a varredura cancelaria o pedido) nem responder 500: a diferença fica na entrega e a equipe é avisada (#1726).
    [SkippableFact]
    public async Task Alteracao_ProvedorForaDoArDeixaADiferencaNaEntregaEAvisaAEquipe()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var foraDoAr = false;
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            if (foraDoAr) throw new HttpRequestException("Provedor temporariamente indisponível");
            var command = c.Arg<CriarPreferenceCommand>();
            return new PreferenceCriadaResult(command.IdempotencyKey!, "https://www.mercadopago.com.br/checkout/teste");
        });
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var (ajuste, _) = await AlteracaoComAcrescimoAsync(factory, client, c);
        foraDoAr = true;

        var resposta = await client.PostAsJsonAsync($"{Rota(ajuste)}/confirmar", new { revisao = 1 });

        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var envio = (await resposta.Content.ReadFromJsonAsync<EnvioComandaCliente>())!;
        envio.Situacao.Should().Be("aguardando_pagamento");
        envio.LinkPagamento.Should().BeNull();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var pendentes = await db.CobrancasPedido.AsNoTracking()
            .Where(x => x.PedidoId == envio.PedidoId && x.Status == StatusCobrancaPedido.Pendente).ToListAsync();
        pendentes.Should().ContainSingle().Which.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        pendentes.Single().Valor.Should().Be(25m);
        (await db.AtendimentoMensagens.AsNoTracking().AnyAsync(m => m.ConversaId == c.ConversaId
            && m.Texto!.Contains("Mercado Pago") && m.Texto.Contains("na entrega"))).Should().BeTrue("a equipe é avisada na conversa");
        // jsonb: o filtro por texto fica em memória.
        var aviso = (await db.NotifEventos.AsNoTracking().Select(e => e.PayloadJson).ToListAsync())
            .Single(p => p.Contains(":aguardando_pagamento"));
        aviso.Should().NotContain("Não há mais valor a pagar").And.Contain("Combine o pagamento");
    }

    // A comanda nova com cupom: o aviso "cupom aplicado" saía antes da cobrança e chegava ao cliente mesmo com o envio
    // desfeito pelo provedor fora do ar (o cupom volta à carteira).
    [SkippableFact]
    public async Task Comanda_ProvedorForaDoArNaoAvisaCupomDeEnvioDesfeito()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var foraDoAr = false;
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            if (foraDoAr) throw new HttpRequestException("Provedor temporariamente indisponível");
            var command = c.Arg<CriarPreferenceCommand>();
            return new PreferenceCriadaResult(command.IdempotencyKey!, "https://www.mercadopago.com.br/checkout/teste");
        });
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var anterior = await Enviar(client, token, 1);
        string codigo;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            var credito = CreditoClienteComanda.Criar(EmpresaId, c.ClienteId, anterior.PedidoId, Guid.NewGuid(), 5m, DateTime.UtcNow);
            db.Add(credito);
            (await db.Pedidos.SingleAsync(p => p.Id == anterior.PedidoId)).Status = "entregue";
            await db.SaveChangesAsync();
            codigo = credito.Codigo;
        }
        var seguinte = await Link(factory, c.ConversaId);
        estado = await Estado(client, seguinte);
        await Salvar(client, seguinte, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId,
            DataEntrega = c.DataEntrega, CodigoCupom = codigo });
        foraDoAr = true;

        var resposta = await client.PostAsJsonAsync($"{Rota(seguinte)}/confirmar", new { revisao = 1 });

        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, await resposta.Content.ReadAsStringAsync());
        using var final = factory.Services.CreateScope();
        var finalDb = final.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        finalDb.SetMobileTenantContext(EmpresaId);
        (await finalDb.Set<CreditoClienteComanda>().AsNoTracking().SingleAsync()).Saldo.Should().Be(5m, "o cupom voltou à carteira");
        (await finalDb.NotifEventos.AsNoTracking().Select(e => e.PayloadJson).ToListAsync())
            .Should().NotContain(p => p.Contains("cupom-usado"), "o envio foi desfeito: o cliente não pode ler que o cupom foi usado");
    }

    /// <summary>
    /// Pedido de encomenda pela comanda (link do sinal de R$ 7,50 pendente) e um link novo com um prato a mais: o envio
    /// é só acréscimo, então o site confirma a alteração sozinho (total R$ 25).
    /// </summary>
    private static async Task<(string Token, Guid LinkId)> AlteracaoComAcrescimoAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory, HttpClient client, Cenario c)
    {
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        (await Enviar(client, token, 1)).Situacao.Should().Be("aguardando_pagamento");
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)] });
        return (ajuste, estado.LinkId);
    }
}
