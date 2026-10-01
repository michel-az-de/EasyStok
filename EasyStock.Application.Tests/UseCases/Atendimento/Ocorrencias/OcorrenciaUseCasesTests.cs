using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Ocorrencias;

/// <summary>S27 (#1186): abertura, resolução e reembolso de ocorrência.</summary>
public class OcorrenciaUseCasesTests
{
    private static readonly DateTime Agora = new(2026, 9, 30, 15, 0, 0, DateTimeKind.Utc);

    private sealed class RelogioFixo(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc, TimeSpan.Zero);
    }

    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly Guid _clienteId = Guid.NewGuid();
    private readonly IOcorrenciaRepository _repo = Substitute.For<IOcorrenciaRepository>();
    private readonly IPedidoRepository _pedidos = Substitute.For<IPedidoRepository>();
    private readonly IConversaRepository _conversas = Substitute.For<IConversaRepository>();
    private readonly IEscaladorConversa _escalador = Substitute.For<IEscaladorConversa>();
    private readonly IOperacaoEventPublisher _eventos = Substitute.For<IOperacaoEventPublisher>();
    private readonly ICobrancaPedidoRepository _cobrancas = Substitute.For<ICobrancaPedidoRepository>();
    private readonly IEstornoPedidoGateway _gateway = Substitute.For<IEstornoPedidoGateway>();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly ICaixaRepository _caixa = Substitute.For<ICaixaRepository>();

    private Pedido NovoPedido()
    {
        var pedido = Pedido.Criar(_empresaId, origem: "whatsapp");
        pedido.ClienteId = _clienteId;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        return pedido;
    }

    private CobrancaPedido CobrancaPaga(Guid pedidoId, decimal valorPago)
    {
        var c = CobrancaPedido.CriarOnline(_empresaId, pedidoId, valorPago, "pref-1", "https://mp/link",
            Agora.AddHours(1), 1, Agora.AddMinutes(-30));
        c.MarcarPaga("pay-123", valorPago, "pix", Agora.AddMinutes(-20));
        _cobrancas.ListarDoPedidoAsync(_empresaId, pedidoId, Arg.Any<CancellationToken>()).Returns(new[] { c });
        return c;
    }

    private Ocorrencia OcorrenciaAberta(Guid pedidoId)
    {
        var o = Ocorrencia.Abrir(_empresaId, pedidoId, _clienteId, null, OrigemOcorrencia.Dona,
            CategoriaOcorrencia.ProdutoImproprio, "bolo chegou azedo", Agora.AddMinutes(-5));
        _repo.ObterAsync(_empresaId, o.Id, Arg.Any<CancellationToken>()).Returns(o);
        return o;
    }

    private AbrirOcorrenciaUseCase Abrir() =>
        new(_repo, _pedidos, _conversas, _escalador, _eventos, _uow, new RelogioFixo(Agora));

    private ReembolsarPedidoUseCase Reembolsar() =>
        new(_cobrancas, _gateway, _crm, _notificador);

    private ResolverOcorrenciaUseCase Resolver() =>
        new(_repo, Reembolsar(), new LancarReembolsoNoCaixaUseCase(_caixa, _cobrancas), _uow, new RelogioFixo(Agora));

    [Fact]
    public async Task AbrirOcorrencia_EscalaConversa()
    {
        var pedido = NovoPedido();
        var conversa = Conversa.Abrir(_empresaId, "5511999990000", Agora.AddHours(-1), clienteId: _clienteId);
        _conversas.ObterPorIdAsync(_empresaId, conversa.Id, Arg.Any<CancellationToken>()).Returns(conversa);
        Ocorrencia? criada = null;
        _repo.When(r => r.AddAsync(Arg.Any<Ocorrencia>(), Arg.Any<CancellationToken>()))
            .Do(ci => criada = ci.Arg<Ocorrencia>());

        var r = await Abrir().ExecuteAsync(new AbrirOcorrenciaInput(
            _empresaId, pedido.Id, OrigemOcorrencia.Avaliacao, CategoriaOcorrencia.Atraso, "nota 1: chegou frio", conversa.Id));

        criada.Should().NotBeNull();
        criada!.Status.Should().Be(StatusOcorrencia.Aberta);
        criada.Origem.Should().Be(OrigemOcorrencia.Avaliacao);
        criada.ClienteId.Should().Be(_clienteId);
        r.Id.Should().Be(criada.Id);
        await _escalador.Received(1).EscalarAsync(_empresaId, conversa, Arg.Is<string>(m => m.Contains("ocorrência")), Agora, Arg.Any<CancellationToken>());
        await _uow.Received(1).CommitAsync();
        await _eventos.Received(1).PublicarAsync(EventosOperacao.OcorrenciaAberta, _empresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AbrirOcorrencia_PedidoInexistenteRejeita()
    {
        var act = () => Abrir().ExecuteAsync(new AbrirOcorrenciaInput(
            _empresaId, Guid.NewGuid(), OrigemOcorrencia.Dona, CategoriaOcorrencia.Outro, "x", null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _repo.DidNotReceive().AddAsync(Arg.Any<Ocorrencia>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReembolsarPedido_ChamaMercadoPagoComPagamentoExternoId()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _gateway.EstornarAsync("pay-123", 30m, ocorrencia.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Ok("ref-9"));

        var r = await Reembolsar().ExecuteAsync(ocorrencia, 30m, "bolo azedo", Agora);

        r.Situacao.Should().Be(SituacaoReembolso.Efetuado);
        ocorrencia.ReembolsoValor.Should().Be(30m);
        ocorrencia.ReembolsoIdSolicitacao.Should().Be("ref-9");
        ocorrencia.ReembolsoEm.Should().Be(Agora);
        await _crm.Received(1).AdicionarNotaAsync(
            Arg.Is<ClienteNota>(n => n.ClienteId == _clienteId && n.PedidoId == pedido.Id && n.Texto.StartsWith("reembolso de R$ 30,00: bolo azedo")),
            Arg.Any<CancellationToken>());
        await _notificador.Received(1).EnfileirarEventoAsync(TipoEventoNotificacao.ReembolsoEfetuado, _empresaId,
            Arg.Any<string>(), ocorrencia.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReembolsarPedido_ValorMaiorQuePagoRejeita()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);

        var act = () => Reembolsar().ExecuteAsync(ocorrencia, 80.01m, "x", Agora);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _gateway.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ReembolsarPedido_SemCobrancaOnlinePagaPedeReembolsoManual()
    {
        var pedido = NovoPedido();
        _cobrancas.ListarDoPedidoAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<CobrancaPedido>());
        var ocorrencia = OcorrenciaAberta(pedido.Id);

        var r = await Reembolsar().ExecuteAsync(ocorrencia, 25m, "pix manual", Agora);

        r.Situacao.Should().Be(SituacaoReembolso.ManualNecessario);
        r.Codigo.Should().Be(ReembolsarPedidoUseCase.CodigoReembolsoManual);
        ocorrencia.ReembolsoValor.Should().Be(25m);
        ocorrencia.ReembolsoEm.Should().BeNull();
        await _gateway.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
    }

    [Fact]
    public async Task ReembolsarPedido_GatewayRecusaNaoGravaReembolso()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _gateway.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("insufficient_funds"));

        var r = await Reembolsar().ExecuteAsync(ocorrencia, 10m, "x", Agora);

        r.Situacao.Should().Be(SituacaoReembolso.Falhou);
        ocorrencia.ReembolsoEm.Should().BeNull();
        await _crm.DidNotReceiveWithAnyArgs().AdicionarNotaAsync(default!, default);
    }

    [Fact]
    public async Task ResolverOcorrencia_SemReembolsoNaoChamaGateway()
    {
        var pedido = NovoPedido();
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        var usuario = Guid.NewGuid();

        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, ocorrencia.Id, usuario, "cupom de 10% na próxima", Reembolsar: false, Valor: null));

        r!.Ocorrencia.Status.Should().Be("resolvida");
        ocorrencia.Resolucao.Should().Be("cupom de 10% na próxima");
        ocorrencia.ResolvidaPorUsuarioId.Should().Be(usuario);
        ocorrencia.ResolvidaEm.Should().Be(Agora);
        await _gateway.DidNotReceiveWithAnyArgs().EstornarAsync(default!, default, default!, default);
        await _caixa.DidNotReceiveWithAnyArgs().AddMovimentoAsync(default!);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ResolverOcorrencia_ComReembolsoSemValorUsaTotalPago()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _gateway.EstornarAsync("pay-123", 80m, ocorrencia.Id.ToString(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Ok("ref-1"));

        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, ocorrencia.Id, Guid.NewGuid(), "devolvido", Reembolsar: true, Valor: null));

        r!.Reembolso!.Situacao.Should().Be(SituacaoReembolso.Efetuado);
        ocorrencia.Status.Should().Be(StatusOcorrencia.Resolvida);
        ocorrencia.ReembolsoValor.Should().Be(80m);
        // F14 (#1244): a devolução sai do caixa do dia, no método da cobrança paga.
        await _caixa.Received(1).AddMovimentoAsync(Arg.Is<MovimentoCaixa>(m =>
            m.Tipo == "saida" && m.Valor == 80m && m.Metodo == "pix"
            && m.Origem == LancarReembolsoNoCaixaUseCase.Origem && m.Referencia == ocorrencia.Id.ToString()));
    }

    [Fact]
    public async Task ResolverOcorrencia_EstornoRecusadoMantemAberta()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _gateway.EstornarAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(EstornoPedidoResult.Falha("erro"));

        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, ocorrencia.Id, Guid.NewGuid(), "devolvido", Reembolsar: true, Valor: 10m));

        r!.Reembolso!.Situacao.Should().Be(SituacaoReembolso.Falhou);
        ocorrencia.Status.Should().Be(StatusOcorrencia.Aberta);
        await _caixa.DidNotReceiveWithAnyArgs().AddMovimentoAsync(default!);
        await _uow.DidNotReceive().CommitAsync();
    }

    [Fact]
    public async Task ResolverOcorrencia_InexistenteDevolveNull()
    {
        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, Guid.NewGuid(), Guid.NewGuid(), "x", false, null));

        r.Should().BeNull();
    }
}
