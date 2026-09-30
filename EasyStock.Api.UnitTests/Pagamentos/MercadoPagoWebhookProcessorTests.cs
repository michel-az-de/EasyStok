using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

/// <summary>
/// S32: processor do webhook do Mercado Pago. O corpo da notificação só traz o id; o estado do pagamento
/// vem sempre de <c>GET v1/payments/{id}</c> (nunca do corpo). Aprovado com valor cheio confirma o pedido
/// pela S11; repetido é no-op; valor menor e recusado não mudam o status; estorno marca a cobrança.
/// </summary>
public class MercadoPagoWebhookProcessorTests
{
    private static readonly Dictionary<string, string?> SemHeaders = new();

    [Fact]
    public async Task ApprovedConfirmaPedido()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
        f.Cobranca.PagamentoExternoId.Should().Be(MercadoPagoWebhookFixture.PagamentoId);
        var pagamento = f.Pedido.Pagamentos.Should().ContainSingle().Subject;
        pagamento.Referencia.Should().Be(MercadoPagoWebhookFixture.PagamentoId);
        pagamento.Metodo.Should().Be("credito");
        await f.MpClient.Received(1).ConsultarPagamentoAsync(MercadoPagoWebhookFixture.PagamentoId, Arg.Any<CancellationToken>());
        await f.Publicador.Received(1).PublicarAsync(
            f.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", f.Pedido.Id, Arg.Any<PedidoPagoEvent>(),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DuplicadoNoOp()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        var processor = f.Processor();
        await processor.ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(action: "payment.created"), SemHeaders);
        f.Publicador.ClearReceivedCalls();

        await processor.ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(action: "payment.updated"), SemHeaders);

        f.Pedido.Pagamentos.Should().ContainSingle("o mesmo data.id não registra o pagamento duas vezes");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Publicador.ReceivedCalls().Should().BeEmpty("o segundo aviso não publica pedido.pago de novo");
    }

    [Fact]
    public async Task ValorMenorNaoConfirma()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 20m);

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        f.Cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Cobranca.Motivo.Should().StartWith("valor_menor");
        f.Eventos.Should().ContainSingle(e => e.Tipo == "pagamento_divergente");
    }

    [Theory]
    [InlineData(PagamentoMercadoPago.Rejected)]
    [InlineData(PagamentoMercadoPago.Cancelled)]
    public async Task RejectedNaoMudaStatus(string status)
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(status, 25m);

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        f.Cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        f.Cobranca.Motivo.Should().Be($"pagamento_recusado: pagamento {MercadoPagoWebhookFixture.PagamentoId} ({status})");
    }

    [Fact]
    public async Task RefundedMarcaEstornoNaCobranca()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        var processor = f.Processor();
        await processor.ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);
        f.PagamentoNaFonte(PagamentoMercadoPago.Refunded, 25m);

        await processor.ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        f.Cobranca.Status.Should().Be(StatusCobrancaPedido.Estornada);
    }

    [Fact]
    public async Task CorpoDizApprovedMasAFonteDizPending_NaoConfirma()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Pending, 25m);
        var forjado = MercadoPagoWebhookFixture.Notificacao()
            .Replace("\"type\":\"payment\"", "\"type\":\"payment\",\"status\":\"approved\",\"transaction_amount\":25");

        await f.Processor().ProcessarAsync(forjado, SemHeaders);

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento, "o estado vem da consulta, não do corpo");
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Theory]
    [InlineData("merchant_order")]
    [InlineData("subscription_preapproval")]
    public async Task TopicoDiferenteDePayment_IgnoraSemConsultar(string tipo)
    {
        var f = new MercadoPagoWebhookFixture();

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(tipo: tipo), SemHeaders);

        await f.MpClient.DidNotReceiveWithAnyArgs().ConsultarPagamentoAsync(default!, default);
    }

    [Theory]
    [InlineData("../users/me")]
    [InlineData("12\\n34")]
    [InlineData("")]
    public async Task IdForaDoFormato_IgnoraSemConsultar(string dataId)
    {
        var f = new MercadoPagoWebhookFixture();

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(dataId: dataId), SemHeaders);

        await f.MpClient.DidNotReceiveWithAnyArgs().ConsultarPagamentoAsync(default!, default);
    }

    [Fact]
    public async Task PagamentoDeOutraOrigem_Ignora()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m, externalReference: "fatura-saas-123");

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        await f.CobrancaRepo.DidNotReceiveWithAnyArgs().ObterEmpresaIdDoPedidoAsync(default, default);
    }

    [Fact]
    public async Task PagamentoInexistenteNaFonte_Ignora()
    {
        var f = new MercadoPagoWebhookFixture();

        await f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task FalhaNaConsulta_Propaga()
    {
        var f = new MercadoPagoWebhookFixture();
        f.MpClient.ConsultarPagamentoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Task<PagamentoMercadoPago?>>(_ => throw new HttpRequestException("mp fora"));

        var act = () => f.Processor().ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), SemHeaders);

        await act.Should().ThrowAsync<HttpRequestException>("o controller responde 500 e o Mercado Pago reenvia");
    }
}
