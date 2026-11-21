using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EasyStock.Api.IntegrationTests.Atendimento;

/// <summary>
/// #2285: o POST de <c>/api/webhooks/mercadopago</c> com Postgres real, de ponta a ponta: assinatura (x-signature),
/// consulta do pagamento na fonte (dublê do Mercado Pago), pedido pago, linha do Caixa/Extrato e a ficha do cliente
/// depois da conversa com pedido. Só a fronteira externa (Mercado Pago) é dublê.
/// </summary>
public sealed partial class PedidoConversaConcorrenciaTests
{
    private const string SegredoWebhookMp = "segredo-webhook-mp-de-teste-descartavel";
    private const string PagamentoMpId = "20285001";

    [SkippableFact]
    public async Task WebhookMp_PagamentoAprovado_PagaOPedidoEEntraUmaVezNoExtrato_MesmoComRepeticao()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        await using var factory = ComSegredoWebhook(CriarFactory(mp, NivelAcesso.Gerente));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var pedidoId = await SemearPedidoAsync(factory, c.ClienteId, 38m, "preferencia-2285", 38m);
        mp.ConsultarPagamentoAsync(PagamentoMpId, Arg.Any<CancellationToken>())
            .Returns(new PagamentoMercadoPago(PagamentoMpId, "approved", null, pedidoId.ToString(), 38m,
                DateTime.UtcNow, "pix", "bank_transfer"));
        var corpo = CorpoPagamentoMp("notificacao-2285-a", PagamentoMpId);

        using var primeira = await PostarWebhookMp(client, corpo, PagamentoMpId);
        primeira.StatusCode.Should().Be(HttpStatusCode.OK, await primeira.Content.ReadAsStringAsync());

        var pago = await LerPedidoAsync(factory, pedidoId);
        pago.Status.Should().Be("aguardando", "o pedido pago sai de aguardando_pagamento e entra na fila");
        pago.TotalPago.Should().Be(38m);
        pago.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(38m);
        (await LerCobrancaAsync(factory, pedidoId)).Status.Should().Be(StatusCobrancaPedido.Paga);

        // Mesmo evento reentregue: o dedup por id da notificação responde 200 e não processa de novo.
        using var repetida = await PostarWebhookMp(client, corpo, PagamentoMpId);
        repetida.StatusCode.Should().Be(HttpStatusCode.OK);
        // O Mercado Pago também reenvia o mesmo pagamento com outro id de notificação: o use case não duplica.
        using var outraNotificacao = await PostarWebhookMp(client, CorpoPagamentoMp("notificacao-2285-b", PagamentoMpId), PagamentoMpId);
        outraNotificacao.StatusCode.Should().Be(HttpStatusCode.OK);

        var depois = await LerPedidoAsync(factory, pedidoId);
        depois.Pagamentos.Should().ContainSingle("a repetição não duplica o recebimento");
        depois.TotalPago.Should().Be(38m);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            (await db.WebhookRecebidos.IgnoreQueryFilters().CountAsync(w => w.Provedor == "MercadoPago"))
                .Should().Be(2, "uma linha por id de notificação, não por entrega");
        }

        // Extrato e Caixa (#2262): uma entrada só, com o valor e a origem do pedido.
        var hoje = EasyStock.Application.Common.HorarioBrasil.Hoje().ToString("yyyy-MM-dd");
        var linhas = await LinhasDoCaixaAsync(client, $"desde={hoje}&ate={hoje}");
        var entrada = linhas.Should().ContainSingle("o recebimento do Mercado Pago aparece uma vez no Extrato").Subject;
        entrada.GetProperty("tipo").GetString().Should().Be("pagamento");
        entrada.GetProperty("origem").GetString().Should().Be("Pedido");
        entrada.GetProperty("metodo").GetString().Should().Be("pix");
        entrada.GetProperty("valor").GetDecimal().Should().Be(38m);
        using var dia = JsonDocument.Parse(await client.GetStringAsync("/api/caixa/dia"));
        dia.RootElement.GetProperty("data").GetProperty("totalPagamentosPedidos").GetDecimal().Should().Be(38m);
    }

    [SkippableFact]
    public async Task WebhookMp_AssinaturaInvalidaOuAusente_Retorna401ENaoPagaOPedido()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        await using var factory = ComSegredoWebhook(CriarFactory(mp, NivelAcesso.Gerente));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        var pedidoId = await SemearPedidoAsync(factory, c.ClienteId, 38m, "preferencia-2285-b", 38m);
        mp.ConsultarPagamentoAsync(PagamentoMpId, Arg.Any<CancellationToken>())
            .Returns(new PagamentoMercadoPago(PagamentoMpId, "approved", null, pedidoId.ToString(), 38m,
                DateTime.UtcNow, "pix", "bank_transfer"));
        var corpo = CorpoPagamentoMp("notificacao-2285-c", PagamentoMpId);

        using var semAssinatura = await client.PostAsync("/api/webhooks/mercadopago",
            new StringContent(corpo, Encoding.UTF8, "application/json"));
        using var segredoErrado = await PostarWebhookMp(client, corpo, PagamentoMpId, segredo: "outro-segredo");
        using var dataIdAdulterado = await PostarWebhookMp(client, corpo, "20285999");

        semAssinatura.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        segredoErrado.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        dataIdAdulterado.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a assinatura vale para o data.id do corpo");
        var pedido = await LerPedidoAsync(factory, pedidoId);
        pedido.Status.Should().Be("aguardando_pagamento");
        pedido.Pagamentos.Should().BeEmpty();
        await mp.DidNotReceiveWithAnyArgs().ConsultarPagamentoAsync(default!, default);
    }

    /// <summary>
    /// Ficha do cliente (dossiê) e histórico da conversa depois de uma conversa que virou pedido pago: o dossiê pela
    /// conversa e pelo cadastro mostram o mesmo pedido, o favorito e o total, e o histórico guarda a mensagem do cliente.
    /// </summary>
    [SkippableFact]
    public async Task Ficha_DepoisDeConversaComPedidoPago_MostraPedidoTotalEHistorico()
    {
        Skip.If(!_isAvailable, "Docker/PostgreSQL unavailable");
        var mp = Provedor(new());
        await using var factory = ComSegredoWebhook(CriarFactory(mp, NivelAcesso.Gerente));
        using var client = factory.CreateClient();
        var c = await SemearAsync(factory);
        await GravarEntradaAsync(factory, c.ConversaId, "oi, quero um brigadeiro");

        using var gerar = await client.PostAsJsonAsync($"/api/atendimento/conversas/{c.ConversaId}/pedido", new
        {
            clienteId = c.ClienteId, vinculoRevisao = c.VinculoRevisao,
            itens = new[] { new { cardapioItemId = c.CardapioItemId, qtd = 2 } },
            janelaId = c.JanelaId,
            dataEntrega = c.DataEntrega.ToString("yyyy-MM-dd"),
            forma = "na_entrega",
        });
        gerar.StatusCode.Should().Be(HttpStatusCode.OK, await gerar.Content.ReadAsStringAsync());
        var pedidoId = await PedidoDaConversaAsync(factory, c.ConversaId);
        var total = (await LerPedidoAsync(factory, pedidoId)).Total;
        using var recebido = await client.PostAsJsonAsync($"/api/pedidos/{pedidoId}/pagamentos",
            new { empresaId = EmpresaId, pedidoId, valor = total, metodo = "pix" });
        recebido.StatusCode.Should().Be(HttpStatusCode.OK, await recebido.Content.ReadAsStringAsync());

        using var dossieConversa = JsonDocument.Parse(await client.GetStringAsync($"/api/atendimento/conversas/{c.ConversaId}/dossie"));
        var porConversa = dossieConversa.RootElement.GetProperty("data");
        porConversa.GetProperty("cliente").GetProperty("id").GetGuid().Should().Be(c.ClienteId);
        porConversa.GetProperty("cliente").GetProperty("nome").GetString().Should().Be("Maria");
        porConversa.GetProperty("pedidoEmAndamento").GetProperty("id").GetGuid().Should().Be(pedidoId);
        porConversa.GetProperty("ultimosPedidos").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())
            .Should().ContainSingle().Which.Should().Be(pedidoId);
        porConversa.GetProperty("conversasRecentes").EnumerateArray().Select(x => x.GetProperty("id").GetGuid())
            .Should().Contain(c.ConversaId);

        using var dossieCliente = JsonDocument.Parse(await client.GetStringAsync($"/api/clientes/{c.ClienteId}/dossie"));
        var porCliente = dossieCliente.RootElement.GetProperty("data");
        porCliente.GetProperty("ultimosPedidos").EnumerateArray().Select(p => p.GetProperty("id").GetGuid())
            .Should().Equal(porConversa.GetProperty("ultimosPedidos").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()),
                "a ficha é a mesma pela conversa e pelo cadastro");
        porCliente.GetProperty("totalPedidos").GetInt32().Should().Be(1);
        porCliente.GetProperty("totalGasto").GetDecimal().Should().Be(total);
        porCliente.GetProperty("itemFavorito").GetProperty("nome").GetString().Should().Be("Brigadeiro");

        using var historico = JsonDocument.Parse(await client.GetStringAsync($"/api/atendimento/conversas/{c.ConversaId}/mensagens"));
        historico.RootElement.GetProperty("data").ToString().Should().Contain("oi, quero um brigadeiro");
    }

    private static string CorpoPagamentoMp(string notificacaoId, string pagamentoId) =>
        """{"id":"__NOTIF__","type":"payment","action":"payment.updated","data":{"id":"__PAG__"}}"""
            .Replace("__NOTIF__", notificacaoId).Replace("__PAG__", pagamentoId);

    /// <summary>Assina como o Mercado Pago: HMAC-SHA256 de <c>id:{data.id};request-id:{x-request-id};ts:{ts};</c>.</summary>
    private static Task<HttpResponseMessage> PostarWebhookMp(
        HttpClient client, string corpo, string dataIdAssinado, string segredo = SegredoWebhookMp)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        const string requestId = "req-2285";
        var manifesto = $"id:{dataIdAssinado};request-id:{requestId};ts:{ts};";
        var v1 = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(manifesto)))
            .ToLowerInvariant();
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/webhooks/mercadopago")
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-signature", $"ts={ts},v1={v1}");
        req.Headers.Add("x-request-id", requestId);
        return client.SendAsync(req);
    }

    private static WebApplicationFactory<Program> ComSegredoWebhook(WebApplicationFactory<Program> factory) =>
        factory.WithWebHostBuilder(b => b.UseSetting("MercadoPago:WebhookSecret", SegredoWebhookMp));

    private static async Task<Pedido> LerPedidoAsync(WebApplicationFactory<Program> factory, Guid pedidoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        return await db.Pedidos.AsNoTracking().Include(p => p.Pagamentos).SingleAsync(p => p.Id == pedidoId);
    }

    private static async Task<CobrancaPedido> LerCobrancaAsync(WebApplicationFactory<Program> factory, Guid pedidoId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        return await db.CobrancasPedido.AsNoTracking().SingleAsync(x => x.PedidoId == pedidoId);
    }

    private static async Task GravarEntradaAsync(WebApplicationFactory<Program> factory, Guid conversaId, string texto)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        db.SetMobileTenantContext(EmpresaId);
        db.AtendimentoMensagens.Add(Mensagem.Entrada(EmpresaId, conversaId, DateTime.UtcNow, TipoConteudoMensagem.Texto, texto, "wamid.2285-ficha"));
        await db.SaveChangesAsync();
    }

    private static async Task<Guid> PedidoDaConversaAsync(WebApplicationFactory<Program> factory, Guid conversaId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
        return (await db.AtendimentoConversas.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == conversaId))
            .PedidoEmAndamentoId!.Value;
    }
}
