using System.Text.Json;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Sales;
using FluentAssertions;
using NSubstitute;

namespace EasyStock.Api.UnitTests.Pagamentos;

public class MercadoPagoOrderWebhookTests
{
    private static readonly Dictionary<string, string?> Headers = new();

    private static string Ordem(Guid pedidoId) => JsonSerializer.Serialize(new {
        type = "order", action = "order.processed",
        data = new { id = "ORDTST000000000000000000000001", external_reference = pedidoId.ToString(),
            status = "processed", total_paid_amount = "999.00",
            transactions = new { payments = new[] { new { payment_method = new { id = "pix" } } } } }
    });

    private static void Busca(MercadoPagoWebhookFixture f, params PagamentoMercadoPago[] pagamentos)
    {
        var pix = pagamentos.Select(p => p with { PaymentMethodId = "pix", TransactionId = "E2E-ATUAL" }).ToArray();
        foreach (var p in pagamentos)
            if (f.Pagamentos.TryGetValue(p.Id, out var detalhe))
                f.Pagamentos[p.Id] = detalhe with { PaymentMethodId = "pix", TransactionId = "E2E-ATUAL" };
        f.MpClient.BuscarPagamentosPixDaOrdemAsync("ORDTST000000000000000000000001", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PagamentoMercadoPago>>(pix));
    }

    [Fact]
    public async Task OrdemAprovadaReconsultaPagamentoEConfirmaSemConfiarNoCorpoNemNaBusca()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId] with { Status = "pending", TransactionAmount = 1m });

        var resultado = await f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);

        resultado.Sucesso.Should().BeTrue();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Pedido.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(25m);
        await f.MpClient.Received(1).ConsultarPagamentoAsync(MercadoPagoWebhookFixture.PagamentoId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrdemDizProcessadaMasFontePendenteNaoBaixa()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Pending, 25m);
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]);

        var acao = () => f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.MpClient.Received(1).ConsultarPagamentoAsync(MercadoPagoWebhookFixture.PagamentoId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrdemEPaymentRepetidosNaoDuplicamRecebimento()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]);
        var processor = f.Processor();

        await processor.ProcessarAsync(Ordem(f.Pedido.Id), Headers);
        await processor.ProcessarAsync(Ordem(f.Pedido.Id), Headers);
        await processor.ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), Headers);

        f.Pedido.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(25m);
        await f.MpClient.Received(2).BuscarPagamentosPixDaOrdemAsync("ORDTST000000000000000000000001", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OrdemAindaSemPagamentoNaFontePedeReenvio()
    {
        var f = new MercadoPagoWebhookFixture();
        Busca(f);

        var acao = () => f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task BuscaNaoPodeApontarPagamentoDeOutroPedido()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m, externalReference: Guid.NewGuid().ToString());
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]);

        var acao = () => f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        await f.MpClient.DidNotReceive().ConsultarPagamentoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task DetalheDoPagamentoDeOutroPedidoNaoPodeSerConciliado()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]);
        f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId] = f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]
            with { ExternalReference = Guid.NewGuid().ToString() };

        var acao = () => f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.CobrancaRepo.DidNotReceive().ObterEmpresaIdDoPedidoAsync(
            Arg.Is<Guid>(id => id != f.Pedido.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TentativaRecusadaAntigaNaoAlteraACobrancaDaOrdem()
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Rejected, 25m);
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]);

        var acao = () => f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);

        await acao.Should().ThrowAsync<InvalidOperationException>();
        f.Cobranca.Motivo.Should().BeNull();
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Fact]
    public async Task OrdemAindaNaoProcessadaNaoConsultaNemBaixa()
    {
        var f = new MercadoPagoWebhookFixture();
        var corpo = Ordem(f.Pedido.Id).Replace("order.processed", "order.action_required");
        var resultado = await f.Processor().ProcessarAsync(corpo, Headers);

        resultado.Sucesso.Should().BeFalse();
        await f.MpClient.DidNotReceive().BuscarPagamentosPixDaOrdemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SinalHistoricoNaoSatisfazOrdemComplementarPendenteOuAusente(bool aparecePendente)
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        var processor = f.Processor();
        await processor.ProcessarAsync(MercadoPagoWebhookFixture.Notificacao(), Headers);
        var extra = new EasyStock.Domain.Entities.PedidoItem { Id = Guid.NewGuid(), PedidoId = f.Pedido.Id,
            Nome = "Complemento", Quantidade = 1, PrecoUnitario = 25m };
        extra.RecalcularSubtotal(); f.Pedido.Itens.Add(extra); f.Pedido.RecalcularTotal();
        var complemento = EasyStock.Domain.Entities.Pagamentos.CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id,
            25m, "pref-complemento", "https://mp.test/complemento", MercadoPagoWebhookFixture.Agora.AddMinutes(30),
            1, MercadoPagoWebhookFixture.Agora);
        f.Cobrancas.Add(complemento);
        f.PagamentoNaFonte(PagamentoMercadoPago.Pending, 25m, id: "20359979");
        var sinal = f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId] with { TransactionId = "E2E-A" };
        var atual = f.Pagamentos["20359979"] with { PaymentMethodId = "pix", TransactionId = "E2E-B" };
        f.Pagamentos["20359979"] = atual;
        f.MpClient.BuscarPagamentosPorReferenciaAsync(f.Pedido.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PagamentoMercadoPago>>(aparecePendente ? [sinal, atual] : [sinal]));
        f.MpClient.BuscarPagamentosPixDaOrdemAsync("ORDTST000000000000000000000001", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PagamentoMercadoPago>>(aparecePendente ? [atual] : []));

        var acao = () => processor.ProcessarAsync(Ordem(f.Pedido.Id), Headers);
        await acao.Should().ThrowAsync<InvalidOperationException>();
        f.Pedido.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(25m);
        complemento.EstaPendente.Should().BeTrue();
    }

    [Fact]
    public async Task OrdemSemPedidoConhecidoNaoConsultaProvedor()
    {
        var f = new MercadoPagoWebhookFixture();
        var resultado = await f.Processor().ProcessarAsync(Ordem(Guid.NewGuid()), Headers);

        resultado.Sucesso.Should().BeFalse();
        await f.MpClient.DidNotReceive().BuscarPagamentosPixDaOrdemAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("E2E-OUTRO")]
    [InlineData("e2e-atual")]
    public async Task DetalheComE2EDivergenteNaoBaixa(string? e2e)
    {
        var f = new MercadoPagoWebhookFixture();
        f.PagamentoNaFonte(PagamentoMercadoPago.Approved, 25m);
        Busca(f, f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]);
        f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId] = f.Pagamentos[MercadoPagoWebhookFixture.PagamentoId]
            with { TransactionId = e2e };
        var acao = () => f.Processor().ProcessarAsync(Ordem(f.Pedido.Id), Headers);
        await acao.Should().ThrowAsync<InvalidOperationException>();
        f.Pedido.Pagamentos.Should().BeEmpty();
    }
}
