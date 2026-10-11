using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.RegistrarEntradaEstoque;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

public sealed partial class PedidoConversaConcorrenciaTests
{
    private static IMercadoPagoClient Provedor(ConcurrentDictionary<string, CriarPreferenceCommand> preferencias)
    {
        var mp = Substitute.For<IMercadoPagoClient>();
        mp.CriarPreferenceAsync(Arg.Any<CriarPreferenceCommand>(), Arg.Any<CancellationToken>()).Returns(c =>
        {
            var command = c.Arg<CriarPreferenceCommand>();
            preferencias.TryAdd(command.IdempotencyKey!, command);
            return new PreferenceCriadaResult(command.IdempotencyKey!, "https://www.mercadopago.com.br/checkout/teste");
        });
        return mp;
    }

    private static async Task<string> Link(WebApplicationFactory<Program> factory, Guid conversaId)
    {
        var token = Guid.NewGuid().ToString("N");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.LinksCardapioConversa.Add(LinkCardapioConversa.Gerar(EmpresaId, conversaId, AcessoChatSite.HashDoToken(token), DateTime.UtcNow));
        await db.SaveChangesAsync();
        return token;
    }

    private static string Rota(string token) => $"/api/storefront/cardapio-conversa/{token}";
    private static async Task<ComandaClienteResult> Estado(HttpClient client, string token) =>
        (await client.GetFromJsonAsync<ComandaClienteResult>($"{Rota(token)}/comanda"))!;
    private static async Task Salvar(HttpClient client, string token, ComandaClienteResult estado, RascunhoComandaCliente dados)
    {
        var resposta = await client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { estado.Revisao, Rascunho = dados });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
    }
    private static async Task<EnvioComandaCliente> Enviar(HttpClient client, string token, int revisao)
    {
        var resposta = await client.PostAsJsonAsync($"{Rota(token)}/confirmar", new { revisao });
        resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        return (await resposta.Content.ReadFromJsonAsync<EnvioComandaCliente>())!;
    }
    private static async Task ConfirmarPagamento(WebApplicationFactory<Program> factory, Guid pedidoId, string pagamentoId, decimal valor)
    {
        using var scope = factory.Services.CreateScope();
        var uc = scope.ServiceProvider.GetRequiredService<ConfirmarPagamentoPedidoUseCase>();
        var input = new ConfirmarPagamentoPedidoInput(pedidoId, pagamentoId, "approved", valor, "pix");
        (await uc.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.Confirmado);
        (await uc.ExecuteAsync(input)).Situacao.Should().Be(SituacaoConfirmacaoPagamento.JaConfirmado);
    }

    [SkippableFact]
    public async Task Comanda_SelecaoCompartilhadaEnvioRepetidoECobrancaSoDoAcrescimo()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var produtoId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var seed = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            seed.SetMobileTenantContext(EmpresaId);
            var agora = DateTime.UtcNow;
            var categoriaId = Guid.NewGuid();
            seed.Set<Categoria>().Add(new Categoria { Id = categoriaId, EmpresaId = EmpresaId, Nome = "Doces", CriadoEm = agora, AlteradoEm = agora });
            seed.Set<Produto>().Add(new Produto { Id = produtoId, EmpresaId = EmpresaId, CategoriaId = categoriaId, Nome = "Brigadeiro", Status = StatusProduto.Ativo, CriadoEm = agora, AlteradoEm = agora });
            (await seed.CardapioItens.SingleAsync(i => i.Id == c.CardapioItemId)).VincularProduto(produtoId);
            await seed.SaveChangesAsync();
            await scope.ServiceProvider.GetRequiredService<RegistrarEntradaEstoqueUseCase>().ExecuteAsync(
                new RegistrarEntradaEstoqueCommand(EmpresaId, produtoId, null, 5, 1m, null, agora.AddDays(-1),
                    NaturezaMovimentacaoEstoque.Producao, null, null, null, null, null, null, null, null, null, null, null, null, null));
        }
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        var dados = estado.Rascunho with { Itens = [new(c.CardapioItemId, 1, "sem granulado")], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega };
        await Salvar(client, token, estado, dados);
        var stale = await client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { Revisao = 0, Rascunho = dados });
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var scope = factory.Services.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().Pedidos.CountAsync()).Should().Be(0);
        var envios = await Task.WhenAll(Enviar(client, token, 1), Enviar(client, token, 1));
        envios.Select(e => e.PedidoId).Distinct().Should().ContainSingle();
        envios[0].Saldo.Should().Be(15);
        preferencias.Should().ContainSingle();
        await ConfirmarPagamento(factory, envios[0].PedidoId, "100001", 15);

        var novo = await Link(factory, c.ConversaId);
        var compartilhada = await Estado(client, novo);
        await Salvar(client, novo, compartilhada, compartilhada.Rascunho with { Itens = [new(c.CardapioItemId, 2, "sem granulado")] });
        await Etapa("preparando");
        await Etapa("pronto");
        (await SaldoEstoque()).Should().Be(4);
        var desatualizado = await client.PostAsJsonAsync($"{Rota(novo)}/confirmar", new { revisao = 1 });
        desatualizado.StatusCode.Should().Be(HttpStatusCode.Conflict, "o preparo mudou desde a seleção salva");
        var atual = await Estado(client, novo);
        atual.Rascunho.Itens.Single().Qtd.Should().Be(2, "a conferência preserva a seleção do cliente");
        atual.Rascunho.PedidoAtualizadoEm.Should().NotBe(compartilhada.Rascunho.PedidoAtualizadoEm);
        await Salvar(client, novo, atual, atual.Rascunho);
        var acrescimo = await Enviar(client, novo, 2);
        acrescimo.PedidoId.Should().Be(envios[0].PedidoId);
        acrescimo.Total.Should().Be(25);
        acrescimo.Saldo.Should().Be(10);
        (await Estado(client, novo)).Pedido!.Status.Should().Be("preparando");
        (await SaldoEstoque()).Should().Be(5, "a alteração retorna o estoque antigo antes de refazer o preparo");
        preferencias.Values.Select(p => p.ValorTotal).Should().BeEquivalentTo([15m, 10m]);
        await ConfirmarPagamento(factory, acrescimo.PedidoId, "100002", 10);
        await Etapa("pronto");
        await Etapa("pronto");
        (await SaldoEstoque()).Should().Be(3, "o novo preparo baixa duas unidades uma única vez");
        (await Estado(client, token)).Pedido!.TotalPago.Should().Be(25);
        var consumido = await client.PutAsJsonAsync($"{Rota(token)}/rascunho", new { Revisao = 1, Rascunho = dados });
        consumido.StatusCode.Should().Be(HttpStatusCode.Gone);
        using var finalScope = factory.Services.CreateScope();
        var db = finalScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await db.Pedidos.CountAsync()).Should().Be(1);
        (await db.Set<PedidoPagamento>().CountAsync()).Should().Be(2);

        async Task Etapa(string status)
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<EasyStockDbContext>().SetMobileTenantContext(EmpresaId);
            await scope.ServiceProvider.GetRequiredService<AtualizarStatusPedidoUseCase>().ExecuteAsync(
                new AtualizarStatusPedidoCommand(EmpresaId, envios[0].PedidoId, status, null, null, "web"));
        }
        async Task<decimal> SaldoEstoque()
        {
            using var scope = factory.Services.CreateScope();
            var dbEstoque = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            dbEstoque.SetMobileTenantContext(EmpresaId);
            return (await dbEstoque.Set<ItemEstoque>().Where(i => i.ProdutoId == produtoId).ToListAsync()).Sum(i => i.QuantidadeAtual.Value);
        }
    }

    [SkippableFact]
    public async Task Comanda_ReducaoEmCupomNaoDuplicaCreditoNemPermiteEstornoDoMesmoValor()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        await using var factory = CriarFactory(Provedor(preferencias), NivelAcesso.Admin);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        await ConfirmarPagamento(factory, envio.PedidoId, "200001", 35);
        var ajuste = await Link(factory, c.ConversaId);
        estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Acerto = "cupom" });
        (await Enviar(client, ajuste, 1)).Situacao.Should().Be("alteracao_aguardando_confirmacao");
        var confirmar = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar";
        for (var i = 0; i < 2; i++)
        {
            var resposta = await client.PostAsJsonAsync(confirmar, new { Aceitar = true });
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        }
        string codigo;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var cupom = await db.Set<CreditoClienteComanda>().SingleAsync();
            cupom.Saldo.Should().Be(20); codigo = cupom.Codigo;
            var pagamento = await db.Set<PedidoPagamento>().SingleAsync();
            var estorno = scope.ServiceProvider.GetRequiredService<IEstornosOnlineService>();
            var duplicar = () => estorno.ReservarAsync(new(EmpresaId, envio.PedidoId, Guid.NewGuid(), pagamento.Id, 20,
                "Não pode devolver crédito já emitido", UsuarioId, "Teste", NivelAcesso.Admin));
            await duplicar.Should().ThrowAsync<CobrancaPedidoConflitoException>();
            var creditos = scope.ServiceProvider.GetRequiredService<ICreditoClienteComandaRepository>();
            (await creditos.ObterComLockAsync(EmpresaId, Guid.NewGuid(), codigo, CancellationToken.None)).Should().BeNull();
            var pedido = await db.Pedidos.SingleAsync(); pedido.Status = "entregue"; await db.SaveChangesAsync();
        }
        var seguinte = await Link(factory, c.ConversaId);
        estado = await Estado(client, seguinte);
        estado.Pedido.Should().BeNull();
        await Salvar(client, seguinte, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], JanelaId = c.JanelaId,
            DataEntrega = c.DataEntrega, CodigoCupom = codigo });
        var pagoComCupom = await Enviar(client, seguinte, 1);
        pagoComCupom.Saldo.Should().Be(0); pagoComCupom.LinkPagamento.Should().BeNull();
        preferencias.Should().ContainSingle("o segundo pedido foi coberto pelo cupom");
        (await Estado(client, seguinte)).Pedido!.TotalPago.Should().Be(15);
        using var finalScope = factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        (await finalDb.Set<CreditoClienteComanda>().SingleAsync()).Saldo.Should().Be(5);
        (await finalDb.Set<PedidoPagamento>().SingleAsync()).Valor.Should().Be(35);
        (await finalDb.Set<UsoCreditoClienteComanda>().SingleAsync()).Valor.Should().Be(15);
        using var cancelarScope = factory.Services.CreateScope();
        var cancelar = cancelarScope.ServiceProvider.GetRequiredService<EasyStock.Application.UseCases.CancelarPedido.CancelarPedidoUseCase>();
        await cancelar.ExecuteAsync(new(EmpresaId, pagoComCupom.PedidoId, UsuarioId, "Teste", "Cancelamento do pedido com crédito", NivelSolicitante: NivelAcesso.Admin));
        var carteira = await cancelarScope.ServiceProvider.GetRequiredService<ICreditoClienteComandaRepository>().ListarAsync(EmpresaId, c.ClienteId, CancellationToken.None);
        carteira.Sum(credito => credito.Saldo).Should().Be(20, "o crédito usado no pedido cancelado volta à carteira");
    }

    [SkippableFact]
    public async Task Comanda_ReducaoConfirmaEstornoParcialERepeticaoNaoDevolveDuasVezes()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var preferencias = new ConcurrentDictionary<string, CriarPreferenceCommand>();
        var mp = Provedor(preferencias);
        await using var factory = CriarFactory(mp, NivelAcesso.Admin);
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory); var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        await Salvar(client, token, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 3)], JanelaId = c.JanelaId, DataEntrega = c.DataEntrega });
        var envio = await Enviar(client, token, 1);
        await ConfirmarPagamento(factory, envio.PedidoId, "300001", 35);
        mp.ConsultarPagamentoAsync("300001", Arg.Any<CancellationToken>()).Returns(new PagamentoMercadoPago(
            "300001", "approved", null, envio.PedidoId.ToString(), 35, DateTime.UtcNow, "pix", "bank_transfer"));
        mp.ListarEstornosAsync("300001", Arg.Any<CancellationToken>()).Returns(Array.Empty<EstornoMercadoPagoResult>());
        mp.EstornarAsync("300001", 20, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new EstornoMercadoPagoResult("900001", 20, "approved", "300001", DateTime.UtcNow));
        var ajuste = await Link(factory, c.ConversaId); estado = await Estado(client, ajuste);
        await Salvar(client, ajuste, estado, estado.Rascunho with { Itens = [new(c.CardapioItemId, 1)], Acerto = "estorno" });
        await Enviar(client, ajuste, 1);
        var confirmar = $"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/confirmar";
        for (var i = 0; i < 2; i++)
        {
            var resposta = await client.PostAsJsonAsync(confirmar, new { Aceitar = true });
            resposta.StatusCode.Should().Be(HttpStatusCode.OK, await resposta.Content.ReadAsStringAsync());
        }
        var final = await Estado(client, ajuste);
        final.Envio!.Situacao.Should().Be("estorno_confirmado"); final.Pedido!.TotalPago.Should().Be(15);
        await mp.Received(1).EstornarAsync("300001", 20, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [SkippableFact]
    public async Task Comanda_StreamEnxergaAlteracaoDoAtendenteSemReabrirOLink()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        await using var factory = CriarFactory(Provedor(new())); using var client = factory.CreateClient();
        var c = await SemearAsync(factory); var token = await Link(factory, c.ConversaId);
        var estado = await Estado(client, token);
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        using var resposta = await client.GetAsync($"{Rota(token)}/eventos", HttpCompletionOption.ResponseHeadersRead, prazo.Token);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var leitor = new StreamReader(await resposta.Content.ReadAsStreamAsync(prazo.Token));
        (await leitor.ReadLineAsync(prazo.Token)).Should().Be("event: comanda");
        (await leitor.ReadLineAsync(prazo.Token)).Should().Contain("\"revisao\":0");
        var rascunho = estado.Rascunho with { Itens = [new(c.CardapioItemId, 2)], Observacoes = "Anotação do atendimento" };
        var salvo = await client.PutAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/comandas-cliente/{estado.LinkId}/rascunho",
            new { Revisao = 0, Rascunho = rascunho }, prazo.Token);
        salvo.StatusCode.Should().Be(HttpStatusCode.OK, await salvo.Content.ReadAsStringAsync(prazo.Token));
        string? linha;
        do { linha = await leitor.ReadLineAsync(prazo.Token); } while (linha is not null && !linha.StartsWith("data:", StringComparison.Ordinal));
        linha.Should().Contain("\"revisao\":1");
        (await Estado(client, token)).Rascunho.Observacoes.Should().Be("Anotação do atendimento");
    }
}
