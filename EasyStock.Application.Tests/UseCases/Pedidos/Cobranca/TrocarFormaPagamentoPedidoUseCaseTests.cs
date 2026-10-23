using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;
using EasyStock.Domain.Sales;

namespace EasyStock.Application.Tests.UseCases.Pedidos.Cobranca;

/// <summary>
/// S11, feedback da operadora (26/09/2026): trocar a forma de pagamento sem esperar o link expirar. A
/// cobrança pendente vira <c>Cancelada</c> e nasce outra na forma pedida; pedido pago recusa; pagamento
/// aprovado de uma preferência já cancelada confirma mesmo assim (dinheiro recebido vence).
/// </summary>
public class TrocarFormaPagamentoPedidoUseCaseTests
{
    private static readonly Guid Usuario = Guid.NewGuid();

    [Fact]
    public async Task NaEntregaParaOnlineGeraNovoLink()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var naEntrega = f.AdicionarNaEntrega();

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        naEntrega.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        var nova = f.Cobrancas.Should().ContainSingle(c => c.Status == StatusCobrancaPedido.Pendente).Subject;
        nova.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
        nova.Tentativa.Should().Be(1, "a troca não consome a reemissão do job");
        r.Cobranca.LinkPagamento.Should().Be("https://mp.test/pref-1");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "trocar a forma não cancela o pedido");
        f.Eventos.Should().Contain(e => e.Tipo == "forma_pagamento_trocada" && e.UsuarioId == Usuario);
    }

    [Fact]
    public async Task SemLink_NaEntrega_ColocaPedidoNaFila()
    {
        var f = new CobrancaPedidoFixture();

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        r.Cobranca.LinkPagamento.Should().BeNull();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "pagamento na entrega libera o pedido para a fila");
        f.Preferencias.Should().BeEmpty();
    }

    [Fact]
    public async Task SemLink_NaEntrega_PedidoQueRequerAprovacao_EsperaABaba()
    {
        // #1291: lead liberado fora de área (S14) que paga na entrega passa pela aprovação, como no pago online.
        var f = new CobrancaPedidoFixture();
        f.Pedido.MarcarRequerAprovacao("fora_de_area");

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoAprovacaoBaba,
            "pedido de exceção não vai para a cozinha sem a dona aprovar");
    }

    [Fact]
    public async Task SemLink_NaEntrega_GravaInicioPrevisto()
    {
        // #1230: o pedido que vai pagar na entrega entra na fila; o início previsto vale como no Mercado Pago.
        var f = new CobrancaPedidoFixture();
        var entrega = new DateTime(2026, 9, 29, 18, 0, 0, DateTimeKind.Utc);
        f.Pedido.AgendadoParaEm = entrega;
        f.PrazoQueries.ObterAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>())
            .Returns(new PrazoPreparoPedidoLeitura(null, null, [null], TempoPreparoPadraoMinutos: 60, RespiroMinutos: 40));

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        f.Pedido.InicioPrevistoEm.Should().Be(entrega.AddMinutes(-100));
    }

    /// <summary>
    /// #1717-2 (decisão do Felipe em 13/10: o sinal pode ser baixado à mão por Pix, dinheiro ou maquininha). Com o
    /// provedor dizendo que o link do sinal não foi pago, a troca cancela o link, expira a preferência e o sinal
    /// recebido por fora coloca o pedido na fila, como no pedido comum.
    /// </summary>
    [Fact]
    public async Task EncomendaFutura_SinalSemPagamentoNoProvedor_CancelaOLinkEAceitaOSinalManual()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        var link = f.AdicionarOnline(referencia: "pref-sinal", valor: 12.5m);

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        link.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.AguardandoPagamento, "sem o sinal a encomenda não entra na fila");
        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-sinal", CobrancaPedidoFixture.Agora, Arg.Any<CancellationToken>());

        await f.RegistrarManual().ExecuteAsync(new(f.EmpresaId, f.Pedido.Id, "pix", 12.5m));

        f.Pedido.TotalPago.Should().Be(12.5m);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando, "o sinal recebido por fora coloca a encomenda na fila");
    }

    /// <summary>
    /// #1717-2: só o link conferido no provedor pode ser cancelado. Outro link pendente que nasceu entre a
    /// conferência e a trava não foi consultado e continua recusando a baixa manual.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinkNovoDepoisDaConferencia_ContinuaRecusado(bool encomenda)
    {
        var f = new CobrancaPedidoFixture();
        if (encomenda)
        {
            f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
            f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        }
        var conferido = f.AdicionarOnline(referencia: "pref-conferida");
        var conferencia = Substitute.For<IConferenciaPagamentoPedido>();
        conferencia.ExecuteAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            conferido.Cancelar(GerarCobrancaPedidoUseCase.MotivoValorAlterado, CobrancaPedidoFixture.Agora);
            f.AdicionarOnline(referencia: "pref-nova");
            return new ConferirPagamentoPedidoResult(ConferirPagamentoPedidoUseCase.SituacaoNaoEncontrado, false);
        });

        var act = () => f.Trocar(conferencia).ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*cobrança online pendente*");
        f.Cobrancas.Should().ContainSingle(c => c.EstaPendente).Which.ReferenciaExterna.Should().Be("pref-nova");
        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }

    [Fact]
    public async Task NaEntrega_AvisaACozinhaUmaVezDepoisDeGravar()
    {
        var f = new CobrancaPedidoFixture();
        var gravou = false;
        f.Uow.When(u => u.CommitAsync()).Do(_ => gravou = true);
        f.OperacaoEventos.When(e => e.PublicarAsync(Arg.Any<string>(), Arg.Any<Guid>(),
                Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(_ => gravou.Should().BeTrue("a cozinha só pode reler um pedido já gravado"));
        var input = new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega");

        await f.Trocar().ExecuteAsync(input);
        await f.Trocar().ExecuteAsync(input);

        await f.OperacaoEventos.Received(1).PublicarAsync(EventosOperacao.PedidoMudouStatus, f.EmpresaId,
            Arg.Is<PedidoMudouStatusOperacao>(e => e.PedidoId == f.Pedido.Id
                && e.StatusAntigo == StatusPedidoMapper.AguardandoPagamento
                && e.StatusNovo == StatusPedidoMapper.Aguardando), Arg.Any<CancellationToken>());
        await f.OperacaoEventos.DidNotReceive().PublicarAsync(EventosOperacao.PedidoPago,
            Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NaEntrega_GravacaoFalha_NaoAvisaACozinha()
    {
        var f = new CobrancaPedidoFixture();
        f.Uow.CommitAsync().Returns<Task>(_ => throw new InvalidOperationException("falha no banco"));

        var act = () => f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        await act.Should().ThrowAsync<InvalidOperationException>();
        await f.OperacaoEventos.DidNotReceiveWithAnyArgs().PublicarAsync(default!, default, default!);
    }

    [Fact]
    public async Task PedidoPagoRecusa()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarOnline().MarcarPaga("pay-1", 25m, "pix", CobrancaPedidoFixture.Agora);

        var act = () => f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        (await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()).Which.Codigo.Should().Be("pedido_ja_pago");
    }

    [Fact]
    public async Task ApprovedDaCanceladaConfirma()
    {
        var f = new CobrancaPedidoFixture();
        var antiga = f.AdicionarOnline(referencia: "pref-antiga");
        antiga.Cancelar("troca_forma: na_entrega", CobrancaPedidoFixture.Agora);
        var naEntrega = f.AdicionarNaEntrega();

        var r = await f.Confirmar().ExecuteAsync(new ConfirmarPagamentoPedidoInput(
            f.Pedido.Id, "pay-7", "approved", 25m, "visa", "credit_card", CobrancaPedidoFixture.Agora,
            ReferenciaExterna: "pref-antiga"));

        r.Confirmado.Should().BeTrue();
        antiga.Status.Should().Be(StatusCobrancaPedido.Paga);
        antiga.MetodoPagamento.Should().Be("credito");
        naEntrega.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        naEntrega.Motivo.Should().Contain("pago_por_outra_cobranca");
        f.Pedido.Pagamentos.Should().ContainSingle(p => p.Referencia == "pay-7");
    }

    /// <summary>S32: o link antigo para de aceitar pagamento quando a forma muda.</summary>
    [Fact]
    public async Task TrocaExpiraAPreferenciaAnteriorNoMercadoPago()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline(referencia: "pref-antiga", expiraEm: CobrancaPedidoFixture.Agora.AddSeconds(-30));

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        await f.MpClient.Received(1).ExpirarPreferenciaAsync("pref-antiga", CobrancaPedidoFixture.Agora, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FalhaAoExpirarPreferencia_NaoDesfazATroca()
    {
        var f = new CobrancaPedidoFixture();
        var antiga = f.AdicionarOnline(referencia: "pref-antiga", expiraEm: CobrancaPedidoFixture.Agora.AddSeconds(-30));
        f.MpClient.ExpirarPreferenciaAsync(Arg.Any<string>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new HttpRequestException("mp fora"));

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        antiga.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorMercadoPago);
    }

    [Fact]
    public async Task TrocaSemCobrancaOnlineAnterior_NaoChamaOMercadoPago()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        f.AdicionarNaEntrega();

        await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "online", Usuario, "Operadora"));

        await f.MpClient.DidNotReceiveWithAnyArgs().ExpirarPreferenciaAsync(default!, default, default);
    }

    /// <summary>#1724-6: com o sinal pago online, trocar a forma do restante não é "pedido já pago".</summary>
    [Fact]
    public async Task SinalPagoOnline_TrocaDoRestanteParaNaEntregaCobraSoORestante()
    {
        var f = new CobrancaPedidoFixture();
        var sinal = f.AdicionarOnline(referencia: "pref-sinal", valor: 12.5m);
        sinal.MarcarPaga("pay-sinal", 12.5m, "pix", CobrancaPedidoFixture.Agora);
        f.Pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = f.Pedido.Id, Valor = 12.5m, Metodo = "pix", PagoEm = CobrancaPedidoFixture.Agora });

        var r = await f.Trocar().ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega", Usuario, "Operadora"));

        r.Cobranca.Provedor.Should().Be(CobrancaPedido.ProvedorNaEntrega);
        r.Cobranca.Valor.Should().Be(12.5m, "o sinal de R$ 12,50 já entrou; só o restante fica para a entrega");
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
    }

    [Fact]
    public async Task PedidoQuitado_NaoTrocaAForma()
    {
        var f = new CobrancaPedidoFixture();
        var online = f.AdicionarOnline(referencia: "pref-total");
        online.MarcarPaga("pay-total", 25m, "pix", CobrancaPedidoFixture.Agora);
        f.Pedido.Pagamentos.Add(new PedidoPagamento { Id = Guid.NewGuid(), PedidoId = f.Pedido.Id, Valor = 25m, Metodo = "pix", PagoEm = CobrancaPedidoFixture.Agora });

        var act = () => f.Trocar().ExecuteAsync(new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, "na_entrega"));

        await act.Should().ThrowAsync<CobrancaPedidoConflitoException>()
            .Where(e => e.Codigo == CobrancaPedidoConflitoException.PedidoJaPago);
    }
}
