using EasyStock.Application.Common;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11: o processor do webhook (S32) chama <see cref="ConfirmarPagamentoPedidoUseCase"/>. Pagamento aprovado
/// com o valor esperado leva o pedido à fila, registra o pagamento e publica <c>pedido.pago</c>; repetir é
/// no-op; valor menor não confirma e não lança (o processor responde 200).
/// </summary>
public class ConfirmarPagamentoPedidoUseCaseTests
{
    private static ConfirmarPagamentoPedidoInput Aprovado(Guid pedidoId, decimal valor = 25m) =>
        new(pedidoId, PagamentoExternoId: "pay-9", StatusPagamento: "approved", ValorPago: valor,
            MetodoPagamentoExterno: "pix", TipoPagamentoExterno: "bank_transfer",
            PagoEm: CobrancaPedidoFixture.Agora);

    [Fact]
    public async Task ConfirmaETransita()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = f.AdicionarOnline();

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Confirmado.Should().BeTrue();
        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.Confirmado);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        var pagamento = f.Pedido.Pagamentos.Should().ContainSingle().Subject;
        pagamento.Referencia.Should().Be("pay-9");
        pagamento.Metodo.Should().Be("pix");
        pagamento.Valor.Should().Be(25m);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
        cobranca.PagamentoExternoId.Should().Be("pay-9");
        f.Tenant.Received().SetCurrentTenant(f.EmpresaId);
        await f.Publicador.Received(1).PublicarAsync(
            f.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", f.Pedido.Id,
            Arg.Is<PedidoPagoEvent>(e => e.CobrancaPedidoId == cobranca.Id && e.PagamentoExternoId == "pay-9"),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        await f.Publicador.Received(1).PublicarAsync(
            f.EmpresaId, "pedido.mudou_status", "pedido", f.Pedido.Id, Arg.Any<PedidoMudouStatusEvent>(),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RequerAprovacaoVaiParaAprovacao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        f.Pedido.MarcarRequerAprovacao("fora_de_area");

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Confirmado.Should().BeTrue();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba,
            "pedido de exceção (S12) vai para a aprovação da dona, não direto para a fila");
        await f.Publicador.Received(1).PublicarAsync(
            f.EmpresaId, "pedido.mudou_status", "pedido", f.Pedido.Id,
            Arg.Is<PedidoMudouStatusEvent>(e => e.StatusNovo == StatusPedidoMapper.AguardandoAprovacaoBaba),
            Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CalculaInicioPrevisto()
    {
        // S21: janela de 12:00 (Brasília) e prazo de 100 min (maior preparo 60 + respiro 40) → começa 10:20.
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var dia = new DateOnly(2026, 9, 30);
        f.PrazoQueries.ObterAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(dia, new TimeOnly(12, 0), [45, null],
                TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40));

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        f.Pedido.InicioPrevistoEm.Should().Be(new DateTime(2026, 9, 30, 13, 20, 0, DateTimeKind.Utc));
        HorarioBrasil.ConverterParaBrasilia(f.Pedido.InicioPrevistoEm!.Value)
            .Should().Be(new DateTime(2026, 9, 30, 10, 20, 0), "10:20 no fuso da loja");
        f.Pedido.AtrasoNotificadoEm.Should().BeNull();
        await f.PedidoStorefrontRepo.Received().UpdateAsync(f.Pedido, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemJanelaNemAgendamento_SemInicioPrevisto()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        f.PrazoQueries.ObterAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(null, null, [null], 60, 40));

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Pedido.InicioPrevistoEm.Should().BeNull("pedido para já não tem janela para atrasar");
    }

    [Fact]
    public async Task RepetidoNoOp()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var uc = f.Confirmar();
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id));
        f.Publicador.ClearReceivedCalls();

        var r = await uc.ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Confirmado.Should().BeFalse();
        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.JaConfirmado);
        f.Pedido.Pagamentos.Should().ContainSingle("o segundo webhook não duplica o pagamento");
        f.Publicador.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task SubpagamentoNaoConfirma()
    {
        var f = new CobrancaPedidoFixture();
        var cobranca = f.AdicionarOnline();

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, valor: 20m));

        r.Confirmado.Should().BeFalse();
        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.ValorMenor);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
        f.Pedido.Pagamentos.Should().BeEmpty();
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Motivo.Should().Contain("valor_menor");
        f.Eventos.Should().Contain(e => e.Tipo == "pagamento_divergente");
    }

    [Fact]
    public async Task StatusNaoAprovado_NaoMexeEmNada()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id) with { StatusPagamento = "rejected" });

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.NaoAprovado);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento);
    }

    [Fact]
    public async Task PedidoCancelado_NaoConfirmaEGravaMotivo()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        var cobranca = f.AdicionarOnline();

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PedidoCancelado);
        f.Pedido.Pagamentos.Should().BeEmpty();
        cobranca.Motivo.Should().Contain("pedido_cancelado");
    }

    [Fact]
    public async Task PublicaPedidoPagoAposCommit()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.ClienteNome = "Ana";
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddHours(2);
        f.AdicionarOnline();
        var numero = f.Pedido.Id.ToString("N")[..8].ToUpperInvariant();
        var ordem = new List<string>();
        f.Uow.When(u => u.CommitAsync()).Do(_ => ordem.Add("commit"));
        f.OperacaoEventos.When(p => p.PublicarAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(_ => ordem.Add("publica"));

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        await f.OperacaoEventos.Received(1).PublicarAsync(
            EventosOperacao.PedidoPago, f.EmpresaId,
            Arg.Is<PedidoPagoOperacao>(e =>
                e.PedidoId == f.Pedido.Id &&
                e.Numero == numero &&
                e.Cliente == "Ana" &&
                e.Total == 25m &&
                e.Janela == CobrancaPedidoFixture.Agora.AddHours(2)),
            Arg.Any<CancellationToken>());
        ordem.Should().Contain("commit").And.EndWith("publica", "o evento de UI sai depois do último commit");
    }

    [Fact]
    public async Task CommitFalhaNaoPublicaPedidoPago()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        f.Uow.CommitAsync().Returns<int>(_ => throw new InvalidOperationException("commit falhou"));

        var act = () => f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        await act.Should().ThrowAsync<InvalidOperationException>();
        f.OperacaoEventos.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task SemConfirmacaoNaoPublicaPedidoPago()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, valor: 20m));

        f.OperacaoEventos.ReceivedCalls().Should().BeEmpty();
    }
}
