using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>#1961: a comanda grava o pedido, faz commit e só então cobra; restos #1725-6, #1717-7, #1686-1 e #1717-12.</summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    // #1725-6: com o Mercado Pago lento, a conversa ficava travada (FOR UPDATE) até o provedor responder, e o
    // webhook, o agente e a atendente esperavam junto. O provedor fica parado aqui até outra transação travar a conversa.
    [SkippableFact]
    public async Task Comanda_ConversaNaoFicaTravadaDuranteAChamadaAoProvedor()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var chamado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(async c =>
        {
            var command = c.Arg<CriarPreferenceCommand>();
            chamado.TrySetResult();
            await liberar.Task;
            return new PreferenceCriadaResult(command.IdempotencyKey!, "https://www.mercadopago.com.br/checkout/1725");
        });
        await using var factory = CriarFactory(mp);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });

        var envio = client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao = (await Estado(client, token)).Revisao });
        (await Task.WhenAny(chamado.Task, envio, Task.Delay(TimeSpan.FromSeconds(30)))).Should().Be(chamado.Task,
            "o envio chega ao provedor");

        try
        {
            await using var conexao = new NpgsqlConnection(_pg!.GetConnectionString());
            await conexao.OpenAsync();
            await using var tx = await conexao.BeginTransactionAsync();
            await using (var timeout = new NpgsqlCommand("SET LOCAL lock_timeout = '2s'", conexao, tx))
                await timeout.ExecuteNonQueryAsync();
            await using var travar = new NpgsqlCommand(
                "SELECT \"PedidoEmAndamentoId\" FROM atendimento_conversas WHERE \"Id\" = @id FOR UPDATE", conexao, tx);
            travar.Parameters.AddWithValue("id", c.ConversaId);
            var pedidoEmAndamento = await travar.ExecuteScalarAsync();
            pedidoEmAndamento.Should().NotBeNull("o pedido foi gravado e commitado antes da cobrança");
            await tx.CommitAsync();
        }
        finally
        {
            liberar.TrySetResult();
        }

        var resposta = await envio;
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        var corpo = (await resposta.Content.ReadFromJsonAsync<EnvioComandaCliente>())!;
        corpo.LinkPagamento.Should().Be("https://www.mercadopago.com.br/checkout/1725");
        (await Estado(client, token)).Envio!.LinkPagamento.Should().Be(corpo.LinkPagamento, "o resultado final fica no link");
    }

    // #1717-7: a mesma encomenda cobrava 100% pela comanda e 50% pelo console.
    [SkippableFact]
    public async Task Comanda_EncomendaCobraOSinalComoOConsole()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });

        var envio = await Enviar(client, token, (await Estado(client, token)).Revisao);

        envio.Total.Should().Be(15m);
        envio.Saldo.Should().Be(15m, "o saldo do pedido continua o total; o link cobra só o sinal");
        preferencias.Values.Should().ContainSingle().Which.ValorTotal.Should().Be(7.5m, "encomenda cobra o sinal de 50%");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        var texto = await db.AtendimentoMensagens.AsNoTracking()
            .Where(m => m.ConversaId == c.ConversaId && m.Texto!.Contains("Recebemos seu pedido")).Select(m => m.Texto).SingleAsync();
        texto.Should().Contain("Sinal de R$").And.Contain("7,50").And.Contain("Restante na entrega");
    }

    // #1686-1: com o pedido gerado pelo console, a comanda aberta seguia editável e cada salvar dava 409.
    [SkippableFact]
    public async Task Comanda_PedidoGeradoPeloConsoleExpiraAComandaAbertaDaConversa()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory();
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });

        var gerado = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/pedido", new
        {
            itens = new[] { new { cardapioItemId = c.CardapioItemId, qtd = 2 } },
            janelaId = c.JanelaId,
            dataEntrega = c.DataEntrega.ToString("yyyy-MM-dd"),
            forma = "na_entrega",
        });
        gerado.StatusCode.Should().Be(HttpStatusCode.OK, await gerado.Content.ReadAsStringAsync());

        var comanda = await client.GetAsync($"{Rota(token)}/comanda");
        comanda.StatusCode.Should().Be(HttpStatusCode.Gone, "a comanda aberta virou órfã: o cliente pede um link novo");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        (await db.LinksCardapioConversa.AsNoTracking().SingleAsync(l => l.ConversaId == c.ConversaId))
            .ExpiraEm.Should().BeOnOrBefore(DateTime.UtcNow);
    }

    // #1717-12: pelo WhatsApp o cliente enviava a comanda e não recebia a confirmação na conversa.
    [SkippableFact]
    public async Task Comanda_PorWhatsAppConfirmaOPedidoNaConversaUmaVez()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var enviados = new ConcurrentQueue<string>();
        var cloud = Substitute.For<IWhatsAppCloudClient>();
        cloud.EnviarTextoAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(x =>
        {
            enviados.Enqueue(x.ArgAt<string>(1));
            return new EnvioWhatsAppResult($"wamid.controlado.{Guid.NewGuid():N}");
        });
        await using var factory = ComWhatsApp(CriarFactory(Provedor(new())), cloud);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        using (var scope = factory.Services.CreateScope())
        {
            // O cliente falou há pouco: a janela de 24 h do WhatsApp está aberta.
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            db.SetMobileTenantContext(EmpresaId);
            (await db.AtendimentoConversas.SingleAsync(v => v.Id == c.ConversaId)).RegistrarEntrada(DateTime.UtcNow);
            await db.SaveChangesAsync();
        }
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with
        {
            Itens = [new(c.CardapioItemId, 1, null)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega,
        });
        var revisao = (await Estado(client, token)).Revisao;

        var envio = await Enviar(client, token, revisao);
        (await Enviar(client, token, revisao)).PedidoId.Should().Be(envio.PedidoId, "o retorno idempotente não cria outro pedido");

        enviados.Where(t => t.Contains("Recebemos seu pedido")).Should().ContainSingle("a confirmação sai uma vez pelo canal da conversa")
            .Which.Should().Contain("https://www.mercadopago.com.br/checkout/teste");
    }

    private static WebApplicationFactory<Program> ComWhatsApp(WebApplicationFactory<Program> factory, IWhatsAppCloudClient cloud) =>
        factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("Notifications:WhatsApp:Provider", "meta");
            b.UseSetting("Notifications:WhatsApp:Meta:AccessToken", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:AppSecret", "fixture-controlada-sem-acesso-externo");
            b.UseSetting("Notifications:WhatsApp:Meta:VerifyToken", "fixture-controlada-sem-acesso-externo");
            b.ConfigureTestServices(s => s.AddSingleton(cloud));
        });
}
