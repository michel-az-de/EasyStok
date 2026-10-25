using EasyStock.Application.Common;
using EasyStock.Application.Events.Pedidos;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.UseCases.RegistrarPagamentoPedido;
using EasyStock.Application.Services.Pedidos;
using EasyStock.Application.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using EasyStock.Domain.Entities.Operacao;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Operacao;
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
    [Fact]
    public async Task SinalPago_GeraSaldoManualSemQuitar_AQuitacaoSaiAposSaldo()
    {
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        var sinal = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));
        sinal.Valor.Should().Be(12.50m);

        var confirmado = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, sinal.Valor));

        confirmado.Confirmado.Should().BeTrue();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Pedido.TotalPago.Should().Be(12.50m);
        var saldo = f.Cobrancas.Should().ContainSingle(c => c.Provedor == "na_entrega").Subject;
        saldo.Valor.Should().Be(12.50m);
        saldo.EstaPendente.Should().BeTrue();
        await f.Publicador.DidNotReceive().PublicarAsync(
            f.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", f.Pedido.Id,
            Arg.Any<PedidoPagoEvent>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        var quitacao = QuitacaoPedidoTeste.Criar(f.PedidoRepo, f.CobrancaRepo, f.ConversaRepo,
            f.ImpressaoRepo, f.Publicador, f.OperacaoEventos);
        var registrar = new RegistrarPagamentoPedidoUseCase(f.PedidoRepo, f.Uow,
            NullLogger<RegistrarPagamentoPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(f.PrazoQueries), quitacao);
        await registrar.ExecuteAsync(new RegistrarPagamentoPedidoCommand(
            f.EmpresaId, f.Pedido.Id, "dinheiro", 12.50m, Origem: "web"));

        f.Pedido.TotalPago.Should().Be(25m);
        saldo.Status.Should().Be(StatusCobrancaPedido.Paga);
        saldo.ValorPago.Should().Be(12.50m);
        await f.Publicador.Received(1).PublicarAsync(
            f.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", f.Pedido.Id,
            Arg.Any<PedidoPagoEvent>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    private static ConfirmarPagamentoPedidoInput Aprovado(Guid pedidoId, decimal valor = 25m) =>
        new(pedidoId, PagamentoExternoId: "pay-9", StatusPagamento: "approved", ValorPago: valor,
            MetodoPagamentoExterno: "pix", TipoPagamentoExterno: "bank_transfer",
            PagoEm: CobrancaPedidoFixture.Agora);

    /// <summary>#1724-12: o recebimento gravava a hora da baixa; a data da aprovação no provedor é a que vale.</summary>
    [Fact]
    public async Task PagamentoDoProvedor_GravaADataDaAprovacao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var aprovadoEm = CobrancaPedidoFixture.Agora.AddMinutes(-42);

        (await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id) with { PagoEm = aprovadoEm })).Confirmado.Should().BeTrue();

        f.Pedido.Pagamentos.Should().ContainSingle().Which.PagoEm.Should().Be(aprovadoEm);
    }

    /// <summary>
    /// Revisão da #1979: o recebimento não volta para um dia de Caixa já fechado (o fechamento não o retratou e o
    /// desfazer recusaria por "dia fechado"); entra no instante do registro.
    /// </summary>
    [Fact]
    public async Task PagamentoDoProvedor_AprovadoEmDiaDeCaixaFechado_FicaNoInstanteDoRegistro()
    {
        var f = new CobrancaPedidoFixture();
        var aprovadoEm = CobrancaPedidoFixture.Agora.AddDays(-1);
        var dia = EasyStock.Application.Common.HorarioBrasil.DataOperacional(aprovadoEm);
        f.CaixaRepo.GetFechamentoDoDiaAsync(f.EmpresaId, dia, f.Pedido.LojaId)
            .Returns(FechamentoCaixaFechado(f, dia));
        var registrar = new RegistrarPagamentoPedidoUseCase(f.PedidoRepo, f.Uow, NullLogger<RegistrarPagamentoPedidoUseCase>.Instance,
            new CalculadoraInicioPrevistoPedido(f.PrazoQueries), QuitacaoPedidoTeste.Criar(f.PedidoRepo), f.CaixaRepo);
        var antes = DateTime.UtcNow;

        await registrar.ExecuteAsync(new RegistrarPagamentoPedidoCommand(f.EmpresaId, f.Pedido.Id, "pix", 25m,
            Referencia: "pay-9", ConfirmadoPeloProvedor: true, AprovadoNoProvedorEm: aprovadoEm));

        f.Pedido.Pagamentos.Should().ContainSingle().Which.PagoEm.Should().BeOnOrAfter(antes);
    }

    private static EasyStock.Domain.Entities.FechamentoCaixa FechamentoCaixaFechado(CobrancaPedidoFixture f, DateOnly dia) =>
        EasyStock.Domain.Entities.FechamentoCaixa.Criar(f.EmpresaId, dia, 0, 0, 25, 0, 0, f.Pedido.LojaId);

    /// <summary>
    /// E12 (#1641-4, decisão do Felipe em 23/10): a falha do contador do número do dia não desfaz o pagamento; o pedido
    /// fica sem número (buraco na sequência é aceitável).
    /// </summary>
    [Fact]
    public async Task ContadorDoNumeroQueFalha_NaoDesfazAConfirmacao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var sequencia = Substitute.For<ISequenciaDiariaPedido>();
        sequencia.ProximoAsync(default, default, default).ReturnsForAnyArgs<Task<int>>(_ => throw new InvalidOperationException("upsert do contador falhou"));
        var calculadora = new CalculadoraInicioPrevistoPedido(f.PrazoQueries, new NumeradorPedidoDoDia(sequencia, f.Relogio));

        var r = await f.Confirmar(calculadora: calculadora).ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Confirmado.Should().BeTrue();
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        f.Pedido.Pagamentos.Should().ContainSingle();
        f.Pedido.NumeroDoDia.Should().BeNull();
    }

    [Fact]
    public async Task ContadorDoNumero_NumeraNaConfirmacao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var sequencia = Substitute.For<ISequenciaDiariaPedido>();
        sequencia.ProximoAsync(default, default, default).ReturnsForAnyArgs(7);
        var calculadora = new CalculadoraInicioPrevistoPedido(f.PrazoQueries, new NumeradorPedidoDoDia(sequencia, f.Relogio));

        (await f.Confirmar(calculadora: calculadora).ExecuteAsync(Aprovado(f.Pedido.Id))).Confirmado.Should().BeTrue();

        f.Pedido.NumeroDoDia.Should().Be(7);
    }

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
        // #1724-7: o valor recebido fica registrado (devolvível pelo estorno online), sem confirmar o pedido.
        f.Pedido.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(20m);
        cobranca.Status.Should().Be(StatusCobrancaPedido.Pendente);
        cobranca.Motivo.Should().Contain("valor_menor");
        f.Eventos.Should().Contain(e => e.Tipo == "pagamento_divergente");
    }

    [Fact]
    public async Task LinkAntigoPagoDepoisDeOPedidoAumentar_RegistraOValorEDeixaOSaldoNaEntrega()
    {
        // #1615: o pedido ganhou um item, o link de 15 foi trocado pelo de 25 e o cliente pagou o antigo, que
        // continua pagável no Mercado Pago. O dinheiro ficava retido como valor_menor e o pedido era cancelado.
        var f = new CobrancaPedidoFixture();
        var antiga = CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id, 15m, "pref-antiga",
            "https://mp.test/pref-antiga", CobrancaPedidoFixture.Agora.AddMinutes(30), 1, CobrancaPedidoFixture.Agora.AddMinutes(-5));
        antiga.Cancelar("valor_do_pedido_alterado", CobrancaPedidoFixture.Agora.AddMinutes(-2));
        f.Cobrancas.Add(antiga);
        var nova = f.AdicionarOnline(tentativa: 2, referencia: "pref-nova");

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, valor: 15m));

        r.Confirmado.Should().BeTrue();
        antiga.Status.Should().Be(StatusCobrancaPedido.Paga);
        nova.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        f.Pedido.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(15m);
        f.Pedido.Status.Should().Be(StatusPedidoMapper.Aguardando);
        var saldo = f.Cobrancas.Should().ContainSingle(c => c.Provedor == "na_entrega").Subject;
        saldo.Valor.Should().Be(10m);
        saldo.EstaPendente.Should().BeTrue();
        f.Eventos.Should().Contain(e => e.Tipo == "pagamento_link_antigo", "a dona vê no histórico por que ficou saldo");
    }

    [Fact]
    public async Task LinkAntigoPago_ComOLinkNovoJaVencido_TambemRegistra()
    {
        // Sem cobrança pendente o alvo era a mais recente (25) e o pagamento de 15 voltava a ficar retido.
        var f = new CobrancaPedidoFixture();
        var antiga = CobrancaPedido.CriarOnline(f.EmpresaId, f.Pedido.Id, 15m, "pref-antiga",
            "https://mp.test/pref-antiga", CobrancaPedidoFixture.Agora.AddMinutes(30), 1, CobrancaPedidoFixture.Agora.AddMinutes(-5));
        antiga.Cancelar("valor_do_pedido_alterado", CobrancaPedidoFixture.Agora.AddMinutes(-2));
        f.Cobrancas.Add(antiga);
        var nova = f.AdicionarOnline(tentativa: 2, referencia: "pref-nova");
        nova.Cancelar("forma_trocada", CobrancaPedidoFixture.Agora);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, valor: 15m));

        r.Confirmado.Should().BeTrue();
        antiga.Status.Should().Be(StatusCobrancaPedido.Paga);
        f.Pedido.Pagamentos.Should().ContainSingle().Which.Valor.Should().Be(15m);
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

    /// <summary>
    /// #1889: desde o #1717-3 o job não cancela pedido com conversa, e só pedido com conversa recebe este aviso.
    /// O cancelamento foi de uma pessoa; "o prazo para pagar acabou" dava ao cliente um motivo falso.
    /// </summary>
    [Fact]
    public void AvisoDoPagamentoTardio_NaoAtribuiOCancelamentoAoPrazo()
    {
        EasyStock.Application.Services.Atendimento.AvisoCobrancaConversa.TextoPagamentoTardioEstornado.Should().NotContain("prazo")
            .And.Contain("cancelado").And.Contain("devolvemos");
    }

    [Fact]
    public async Task PagoDepoisDeCanceladoEstornaEAvisa()
    {
        // O cliente pagou depois de o job cancelar o pedido: devolve o dinheiro na hora e avisa na conversa.
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        var conversaId = Guid.NewGuid();
        var cobranca = f.AdicionarOnline(conversaId: conversaId);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PedidoCancelado);
        await f.Estorno.Received(1).EstornarAsync("pay-9", 25m, "estorno-tardio-pay-9", Arg.Any<CancellationToken>());
        cobranca.Motivo.Should().Contain("estorno_automatico").And.Contain("pay-9");
        f.Pedido.Pagamentos.Should().BeEmpty();
        await f.ConversaRepo.Received(1).ObterPorIdAsync(f.EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagoDepoisDeCancelado_RepetidoNaoEstornaDeNovo()
    {
        // O Mercado Pago manda mais de uma notificação por pagamento; a segunda não estorna nem avisa.
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        var conversaId = Guid.NewGuid();
        f.AdicionarOnline(conversaId: conversaId);

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));
        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        await f.Estorno.Received(1).EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await f.ConversaRepo.Received(1).ObterPorIdAsync(f.EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PagoDepoisDeCancelado_EstornoRecusadoLanca()
    {
        // Falha no estorno propaga: o controller responde 500 e o Mercado Pago reenvia a notificação.
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        f.AdicionarOnline(conversaId: Guid.NewGuid());
        f.Estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("estorno_recusado"));

        var act = () => f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        await act.Should().ThrowAsync<EstornoAutomaticoFalhouException>();
        await f.ConversaRepo.DidNotReceive().ObterPorIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>#1724: o estorno automático recusado sumia com a transação; o pedido não mostrava o dinheiro retido.</summary>
    [Fact]
    public async Task PagoDepoisDeCancelado_EstornoRecusadoDeixaTrilhaUmaVezEContinuaPedindoReenvio()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        var cobranca = f.AdicionarOnline(conversaId: Guid.NewGuid());
        f.Estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("saldo_insuficiente"));

        var act = () => f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));
        await act.Should().ThrowAsync<EstornoAutomaticoFalhouException>();
        await act.Should().ThrowAsync<EstornoAutomaticoFalhouException>("o reenvio do provedor tenta de novo");

        f.Eventos.Should().ContainSingle(e => e.Tipo == ConfirmarPagamentoPedidoUseCase.EventoEstornoFalhou)
            .Which.Detalhes.Should().Contain("saldo_insuficiente").And.Contain("25,00");
        cobranca.Motivo.Should().StartWith(ConfirmarPagamentoPedidoUseCase.MarcaEstornoFalhou);
        await f.Estorno.Received(2).EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>#1724: depois da falha registrada, o reenvio que consegue estornar fecha o caso como antes.</summary>
    [Fact]
    public async Task PagoDepoisDeCancelado_ReenvioQueEstornaSubstituiAFalha()
    {
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Cancelado);
        var cobranca = f.AdicionarOnline(conversaId: Guid.NewGuid());
        f.Estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("indisponivel"), EstornoPedidoResult.Ok("estorno-9"));

        await f.Confirmar().Invoking(c => c.ExecuteAsync(Aprovado(f.Pedido.Id))).Should().ThrowAsync<EstornoAutomaticoFalhouException>();
        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PedidoCancelado);
        cobranca.Motivo.Should().Contain("estorno estorno-9");
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

    [Fact]
    public async Task EnfileiraImpressaoNaMesmaTransacao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var ordem = new List<string>();
        ImpressaoPendente? enfileirada = null;
        f.Uow.ExecuteInTransactionSemRetryAsync(
                Arg.Any<Func<CancellationToken, Task<(ConfirmarPagamentoPedidoResult, PedidoPagoOperacao?, ImpressaoPendenteOperacao?, Guid?)>>>(),
                Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                ordem.Add("abre transacao");
                var r = await ci.Arg<Func<CancellationToken, Task<(ConfirmarPagamentoPedidoResult, PedidoPagoOperacao?, ImpressaoPendenteOperacao?, Guid?)>>>()(ci.Arg<CancellationToken>());
                ordem.Add("fecha transacao");
                return r;
            });
        f.ImpressaoRepo.When(r => r.AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>()))
            .Do(ci => { enfileirada = ci.Arg<ImpressaoPendente>(); ordem.Add("enfileira"); });
        f.Uow.When(u => u.CommitAsync()).Do(_ => ordem.Add("commit"));
        f.OperacaoEventos.When(p => p.PublicarAsync(EventosOperacao.ImpressaoPendente, Arg.Any<Guid>(), Arg.Any<object>(), Arg.Any<CancellationToken>()))
            .Do(_ => ordem.Add("sse impressao"));

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        enfileirada.Should().NotBeNull("pedido pago entra na fila de impressão");
        enfileirada!.PedidoId.Should().Be(f.Pedido.Id);
        enfileirada.EmpresaId.Should().Be(f.EmpresaId);
        enfileirada.Tipo.Should().Be(TipoImpressao.Canhoto);
        enfileirada.Status.Should().Be(StatusImpressao.Pendente);
        // O flush do RegistrarPagamentoPedido e o commit final ficam dentro da mesma transação.
        ordem.Should().Equal(
            new[] { "abre transacao", "commit", "enfileira", "commit", "fecha transacao", "sse impressao" },
            "pagamento e impressão são gravados antes de fechar a transação e o SSE sai depois");
        await f.OperacaoEventos.Received(1).PublicarAsync(
            EventosOperacao.ImpressaoPendente, f.EmpresaId,
            Arg.Is<ImpressaoPendenteOperacao>(e => e.ImpressaoId == enfileirada.Id && e.PedidoId == f.Pedido.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SemConfirmacaoNaoEnfileiraImpressao()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();

        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, valor: 20m));

        await f.ImpressaoRepo.DidNotReceive().AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepetidoNaoEnfileiraDeNovo()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var uc = f.Confirmar();
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id));
        f.ImpressaoRepo.ClearReceivedCalls();

        await uc.ExecuteAsync(Aprovado(f.Pedido.Id));

        await f.ImpressaoRepo.DidNotReceive().AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>());
    }

    // ── #1289: segundo pagamento aprovado do mesmo pedido ──────────────────

    [Fact]
    public async Task SegundoPagamento_ComCobrancaJaPaga_EstornaSemLancar()
    {
        // Cobrança paga por P1; o cliente paga de novo (P2). Antes: MarcarPaga lançava e o webhook dava 500 em loop.
        var f = new CobrancaPedidoFixture();
        var conversaId = Guid.NewGuid();
        var cobranca = f.AdicionarOnline(conversaId: conversaId);
        var uc = f.Confirmar();
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id));
        f.ImpressaoRepo.ClearReceivedCalls();
        f.Publicador.ClearReceivedCalls();

        var r = await uc.ExecuteAsync(Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-10" });

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PagamentoDuplicado);
        await f.Estorno.Received(1).EstornarAsync("pay-10", 25m, "estorno-duplicado-pay-10", Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().ContainSingle("o segundo pagamento não é registrado").Which.Referencia.Should().Be("pay-9");
        cobranca.Status.Should().Be(StatusCobrancaPedido.Paga);
        cobranca.PagamentoExternoId.Should().Be("pay-9");
        f.Eventos.Should().Contain(e => e.Tipo == "pagamento_divergente" && e.Detalhes!.Contains("pay-10"));
        await f.ImpressaoRepo.DidNotReceive().AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>());
        f.Publicador.ReceivedCalls().Should().BeEmpty();
        await f.ConversaRepo.Received(1).ObterPorIdAsync(f.EmpresaId, conversaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SegundoPagamento_NaCobrancaCanceladaComPedidoPago_EstornaENaoRegistra()
    {
        // A cobrança paga por P1 e outra cancelada; P2 paga a cancelada. Antes: aceito com PermitirExcedente.
        var f = new CobrancaPedidoFixture();
        var paga = f.AdicionarOnline(referencia: "pref-a");
        var uc = f.Confirmar();
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id) with { ReferenciaExterna = "pref-a" });
        var cancelada = f.AdicionarOnline(referencia: "pref-b");
        cancelada.Cancelar("troca_forma", CobrancaPedidoFixture.Agora);
        f.ImpressaoRepo.ClearReceivedCalls();

        var r = await uc.ExecuteAsync(Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-10", ReferenciaExterna = "pref-b" });

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PagamentoDuplicado);
        await f.Estorno.Received(1).EstornarAsync("pay-10", 25m, "estorno-duplicado-pay-10", Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().ContainSingle();
        cancelada.Status.Should().Be(StatusCobrancaPedido.Cancelada);
        paga.PagamentoExternoId.Should().Be("pay-9");
        await f.ImpressaoRepo.DidNotReceive().AddAsync(Arg.Any<ImpressaoPendente>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pagamento_ComPedidoJaQuitadoManualmente_EstornaENaoRegistra()
    {
        // Pedido já quitado por pagamento registrado no console (TotalPago >= Total) e cobrança online cancelada.
        var f = new CobrancaPedidoFixture(StatusPedidoMapper.Aguardando);
        var cobranca = f.AdicionarOnline();
        cobranca.Cancelar("troca_forma", CobrancaPedidoFixture.Agora);
        f.AdicionarPagamento("manual-1", 25m);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id));

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PagamentoDuplicado);
        await f.Estorno.Received(1).EstornarAsync("pay-9", 25m, "estorno-duplicado-pay-9", Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().ContainSingle().Which.Referencia.Should().Be("manual-1");
        cobranca.Status.Should().Be(StatusCobrancaPedido.Cancelada);
    }

    [Fact]
    public async Task SegundoPagamento_RepetidoNaoEstornaDeNovo()
    {
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var uc = f.Confirmar();
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id));
        var segundo = Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-10" };
        await uc.ExecuteAsync(segundo);

        var r = await uc.ExecuteAsync(segundo);

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PagamentoDuplicado);
        await f.Estorno.Received(1).EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SegundoPagamento_EstornoRecusadoLanca()
    {
        // Mesmo contrato do estorno tardio: o webhook responde 500 e o Mercado Pago reenvia com a mesma chave.
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline();
        var uc = f.Confirmar();
        await uc.ExecuteAsync(Aprovado(f.Pedido.Id));
        f.Estorno.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("estorno_recusado"));

        var act = () => uc.ExecuteAsync(Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-10" });

        await act.Should().ThrowAsync<EstornoAutomaticoFalhouException>();
        f.Pedido.Pagamentos.Should().ContainSingle();
    }

    // ── #2056: link pago depois de devolução online integral ──────────────

    /// <summary>
    /// Devolução online integral confirmada (<c>EstornosOnlineService</c>): a cobrança fica <c>Estornada</c>, o
    /// <c>PedidoPagamento</c> fica e o estorno confirmado tira o valor do saldo.
    /// </summary>
    private static void DevolverIntegral(CobrancaPedidoFixture f, CobrancaPedido cobranca, string pagamentoExterno)
    {
        var pagamento = f.Pedido.Pagamentos.Single(p => p.Referencia == pagamentoExterno);
        cobranca.MarcarEstornada("Devolução integral confirmada no Mercado Pago", CobrancaPedidoFixture.Agora);
        f.Estornos.ListarAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(new List<PedidoEstornoOnline>
        {
            new() { Id = Guid.NewGuid(), EmpresaId = f.EmpresaId, PedidoId = f.Pedido.Id, PagamentoId = pagamento.Id,
                PagamentoExternoId = pagamentoExterno, Valor = pagamento.Valor, Situacao = PedidoEstornoOnline.Confirmado },
        });
        f.Estornos.RecebimentosAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(new List<RecebimentoOnline>
        {
            new(pagamento.Id, cobranca.Id, pagamentoExterno, pagamento.Valor, "pix", pagamento.PagoEm, null, true),
        });
    }

    [Fact]
    public async Task LinkNovo_PagoDepoisDeDevolucaoOnlineIntegral_ConfirmaSemEstornar()
    {
        // O TotalPago cru ainda contava o pagamento devolvido: o novo era tratado como duplicado e devolvido sozinho.
        var f = new CobrancaPedidoFixture();
        var primeira = f.AdicionarOnline(referencia: "pref-1");
        (await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-1", ReferenciaExterna = "pref-1" }))
            .Confirmado.Should().BeTrue();
        DevolverIntegral(f, primeira, "pay-1");
        var nova = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));
        nova.Valor.Should().Be(25m);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id) with
        {
            PagamentoExternoId = "pay-2", PagoEm = CobrancaPedidoFixture.Agora.AddMinutes(10),
        });

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.Confirmado);
        await f.Estorno.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
        f.Pedido.Pagamentos.Select(p => p.Referencia).Should().BeEquivalentTo("pay-1", "pay-2");
        f.Cobrancas.Single(c => c.Id == nova.CobrancaId).Status.Should().Be(StatusCobrancaPedido.Paga);
        primeira.Status.Should().Be(StatusCobrancaPedido.Estornada);
    }

    [Fact]
    public async Task SinalNovo_PagoDepoisDeDevolverOSinal_NaoQuitaEDeixaOSaldoNaEntrega()
    {
        // O TotalPago cru (12,50 devolvidos + 12,50 novos) cobria os 25: o pedido saía quitado e o saldo da porta sumia.
        var f = new CobrancaPedidoFixture();
        f.Pedido.CriadoEm = CobrancaPedidoFixture.Agora;
        f.Pedido.AgendadoParaEm = CobrancaPedidoFixture.Agora.AddDays(2);
        var sinal = await f.Gerar().ExecuteAsync(new GerarCobrancaPedidoInput(f.EmpresaId, f.Pedido.Id));
        (await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, sinal.Valor) with { PagamentoExternoId = "pay-1" }))
            .Confirmado.Should().BeTrue();
        DevolverIntegral(f, f.Cobrancas.Single(c => c.Id == sinal.CobrancaId), "pay-1");
        // A atendente volta o pedido para o link: a cobrança da entrega cai e nasce o sinal de novo.
        var novoSinal = (await f.Trocar(saldos: f.Saldos()).ExecuteAsync(
            new TrocarFormaPagamentoPedidoInput(f.EmpresaId, f.Pedido.Id, TrocarFormaPagamentoPedidoUseCase.FormaOnline))).Cobranca;
        novoSinal.Valor.Should().Be(12.50m);

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id, 12.50m) with { PagamentoExternoId = "pay-2" });

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.Confirmado);
        await f.Estorno.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
        await f.Publicador.DidNotReceive().PublicarAsync(
            f.EmpresaId, PedidoPagoEvent.TipoEvento, "pedido", f.Pedido.Id,
            Arg.Any<PedidoPagoEvent>(), Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
        var saldo = f.Cobrancas.Should().ContainSingle(c => c.Provedor == "na_entrega" && c.EstaPendente).Subject;
        saldo.Valor.Should().Be(12.50m);
    }

    [Fact]
    public async Task SegundoPagamento_ComODevolvidoSoEmPendencia_ContinuaEstornando()
    {
        // Devolução pedida e não confirmada não libera o pedido: a cobrança segue paga e o saldo ainda conta o pagamento.
        var f = new CobrancaPedidoFixture();
        f.AdicionarOnline(referencia: "pref-1");
        await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-1", ReferenciaExterna = "pref-1" });
        var pagamento = f.Pedido.Pagamentos.Single();
        f.Estornos.ListarAsync(f.EmpresaId, f.Pedido.Id, Arg.Any<CancellationToken>()).Returns(new List<PedidoEstornoOnline>
        {
            new() { Id = Guid.NewGuid(), EmpresaId = f.EmpresaId, PedidoId = f.Pedido.Id, PagamentoId = pagamento.Id,
                PagamentoExternoId = "pay-1", Valor = 25m, Situacao = PedidoEstornoOnline.Pendente },
        });

        var r = await f.Confirmar().ExecuteAsync(Aprovado(f.Pedido.Id) with { PagamentoExternoId = "pay-2" });

        r.Situacao.Should().Be(SituacaoConfirmacaoPagamento.PagamentoDuplicado);
        await f.Estorno.Received(1).EstornarAsync("pay-2", 25m, "estorno-duplicado-pay-2", Arg.Any<CancellationToken>());
        f.Pedido.Pagamentos.Should().ContainSingle();
    }
}
