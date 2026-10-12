using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Pagamentos;
using EasyStock.Application.Services.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Ocorrencias;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Domain.Enums.Atendimento;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Application.Tests.Helpers;

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
    private readonly IEstornosOnlineService _estornos = Substitute.For<IEstornosOnlineService>();
    private readonly Guid _pagamentoId = Guid.NewGuid();
    private readonly IClienteCrmRepository _crm = Substitute.For<IClienteCrmRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();

    public OcorrenciaUseCasesTests()
    {
        _uow.SetupExecuteInTransactionSemRetry<Ocorrencia?>();
        _uow.SetupExecuteInTransactionSemRetry<OcorrenciaDto?>();
        _uow.SetupExecuteInTransactionSemRetry<ResolverOcorrenciaResult?>();
    }

    private Pedido NovoPedido()
    {
        var pedido = Pedido.Criar(_empresaId, origem: "whatsapp");
        pedido.ClienteId = _clienteId;
        _pedidos.GetByIdAsync(_empresaId, pedido.Id).Returns(pedido);
        _estornos.ConsultarAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(new EstornosOnlineResult([], []));
        return pedido;
    }

    private CobrancaPedido CobrancaPaga(Guid pedidoId, decimal valorPago)
    {
        var c = CobrancaPedido.CriarOnline(_empresaId, pedidoId, valorPago, "pref-1", "https://mp/link",
            Agora.AddHours(1), 1, Agora.AddMinutes(-30));
        c.MarcarPaga("pay-123", valorPago, "pix", Agora.AddMinutes(-20));
        _cobrancas.ListarDoPedidoAsync(_empresaId, pedidoId, Arg.Any<CancellationToken>()).Returns(new[] { c });
        _estornos.ConsultarAsync(_empresaId, pedidoId, Arg.Any<CancellationToken>()).Returns(new EstornosOnlineResult(
            [new(_pagamentoId, "pix", valorPago, Agora, 0, 0, valorPago, false)], []));
        return c;
    }

    private void ClienteComTelefone(string? telefone = "(11) 99999-0001")
    {
        var cliente = new Cliente { Id = _clienteId, EmpresaId = _empresaId, Nome = "Maria Souza", Telefone = telefone };
        _crm.ObterComTagsAsync(_empresaId, _clienteId, Arg.Any<CancellationToken>()).Returns(cliente);
    }

    private Ocorrencia OcorrenciaAberta(Guid pedidoId)
    {
        var o = Ocorrencia.Abrir(_empresaId, pedidoId, _clienteId, null, OrigemOcorrencia.Dona,
            CategoriaOcorrencia.ProdutoImproprio, "bolo chegou azedo", Agora.AddMinutes(-5));
        _repo.ObterAsync(_empresaId, o.Id, Arg.Any<CancellationToken>()).Returns(o);
        _repo.ObterTravadaAsync(_empresaId, o.Id, Arg.Any<CancellationToken>()).Returns(o);
        return o;
    }

    private AbrirOcorrenciaUseCase Abrir() =>
        new(_repo, _pedidos, _conversas, _escalador, _eventos, _uow, new RelogioFixo(Agora));

    private ReembolsarPedidoUseCase Reembolsar() =>
        new(_cobrancas, _estornos, _crm, _notificador);

    private ResolverOcorrenciaUseCase Resolver() =>
        new(_repo, Reembolsar(), _uow, new RelogioFixo(Agora));

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
        ClienteComTelefone();
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Valor = 30m, Situacao = PedidoEstornoOnline.Confirmado, EstornoExternoId = "ref-9" });

        var resultado = await Resolver().ExecuteAsync(new(_empresaId, ocorrencia.Id, Guid.NewGuid(), "bolo azedo", true, 30m, NivelAcesso.Admin, "Dona"));
        var r = resultado!.Reembolso!;

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
    public async Task ReembolsarPedido_AvisoLevaTelefoneENomeDoCliente()
    {
        // #1292: sem "telefone" no payload o NotificadorService não resolve o destinatário do WhatsApp.
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        ClienteComTelefone();
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Valor = 30m, Situacao = PedidoEstornoOnline.Confirmado, EstornoExternoId = "ref-9" });
        string? payload = null;
        await _notificador.EnfileirarEventoAsync(TipoEventoNotificacao.ReembolsoEfetuado, _empresaId,
            Arg.Do<string>(p => payload = p), ocorrencia.Id, Arg.Any<CancellationToken>());

        await Resolver().ExecuteAsync(new(_empresaId, ocorrencia.Id, Guid.NewGuid(), "bolo azedo", true, 30m, NivelAcesso.Admin, "Dona"));

        payload.Should().NotBeNull();
        var json = System.Text.Json.JsonDocument.Parse(payload!).RootElement;
        json.GetProperty("telefone").GetString().Should().Be("+5511999990001");
        json.GetProperty("nome").GetString().Should().Be("Maria");
        json.GetProperty("numero").GetString().Should().Be(pedido.Id.ToString("N")[..8].ToUpperInvariant());
        json.GetProperty("valor").GetString().Should().Be("R$ 30,00");
    }

    [Fact]
    public async Task ReembolsarPedido_SemTelefoneValidoNaoEnfileiraAviso()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        ClienteComTelefone(telefone: null);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Valor = 30m, Situacao = PedidoEstornoOnline.Confirmado, EstornoExternoId = "ref-9" });

        var resultado = await Resolver().ExecuteAsync(new(_empresaId, ocorrencia.Id, Guid.NewGuid(), "bolo azedo", true, 30m, NivelAcesso.Admin, "Dona"));
        var r = resultado!.Reembolso!;

        r.Situacao.Should().Be(SituacaoReembolso.Efetuado, "o dinheiro já voltou; só o aviso não tem para onde ir");
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task ReembolsarPedido_ValorMaiorQuePagoRejeita()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);

        var act = () => Reembolsar().ExecuteAsync(ocorrencia, 80.01m, "x", Agora, Guid.NewGuid(), "Dona", NivelAcesso.Admin);

        await act.Should().ThrowAsync<UseCaseValidationException>();
        await _estornos.DidNotReceiveWithAnyArgs().SolicitarAsync(default!, default);
    }

    [Fact]
    public async Task ReembolsarPedido_SemCobrancaOnlinePagaPedeReembolsoManual()
    {
        var pedido = NovoPedido();
        _cobrancas.ListarDoPedidoAsync(_empresaId, pedido.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<CobrancaPedido>());
        var ocorrencia = OcorrenciaAberta(pedido.Id);

        var resultado = await Resolver().ExecuteAsync(new(_empresaId, ocorrencia.Id, Guid.NewGuid(), "pix manual", true, 25m, NivelAcesso.Admin, "Dona"));
        var r = resultado!.Reembolso!;

        r.Situacao.Should().Be(SituacaoReembolso.ManualNecessario);
        r.Codigo.Should().Be(ReembolsarPedidoUseCase.CodigoReembolsoManual);
        ocorrencia.ReembolsoValor.Should().Be(25m);
        ocorrencia.ReembolsoEm.Should().BeNull();
        await _estornos.DidNotReceiveWithAnyArgs().SolicitarAsync(default!, default);
    }

    [Fact]
    public async Task ReembolsarPedido_GatewayRecusaNaoGravaReembolso()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Situacao = PedidoEstornoOnline.Recusado });

        var r = await Reembolsar().ExecuteAsync(ocorrencia, 10m, "x", Agora, Guid.NewGuid(), "Dona", NivelAcesso.Admin);

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
            _empresaId, ocorrencia.Id, usuario, "cupom de 10% na próxima", Reembolsar: false, Valor: null, NivelSolicitante: NivelAcesso.Admin));

        r!.Ocorrencia.Status.Should().Be("resolvida");
        ocorrencia.Resolucao.Should().Be("cupom de 10% na próxima");
        ocorrencia.ResolvidaPorUsuarioId.Should().Be(usuario);
        ocorrencia.ResolvidaEm.Should().Be(Agora);
        await _estornos.DidNotReceiveWithAnyArgs().SolicitarAsync(default!, default);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task ResolverOcorrencia_ComReembolsoSemValorUsaTotalPago()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Valor = 80m, Situacao = PedidoEstornoOnline.Confirmado, EstornoExternoId = "ref-1" });

        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, ocorrencia.Id, Guid.NewGuid(), "devolvido", Reembolsar: true, Valor: null, NivelSolicitante: NivelAcesso.Admin));

        r!.Reembolso!.Situacao.Should().Be(SituacaoReembolso.Efetuado);
        ocorrencia.Status.Should().Be(StatusOcorrencia.Resolvida);
        ocorrencia.ReembolsoValor.Should().Be(80m);
    }

    [Fact]
    public async Task ResolverOcorrencia_EstornoRecusadoMantemAberta()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80m);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Situacao = PedidoEstornoOnline.Recusado });

        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, ocorrencia.Id, Guid.NewGuid(), "devolvido", Reembolsar: true, Valor: 10m, NivelSolicitante: NivelAcesso.Admin));

        r!.Reembolso!.Situacao.Should().Be(SituacaoReembolso.Falhou);
        ocorrencia.Status.Should().Be(StatusOcorrencia.Aberta);
        ocorrencia.ReembolsoSolicitadoEm.Should().Be(Agora);
        await _uow.Received(1).CommitAsync();
    }

    [Fact]
    public async Task Atendimento_nao_reembolsa_e_pendente_nao_resolve_ocorrencia()
    {
        var pedido = NovoPedido();
        CobrancaPaga(pedido.Id, 80);
        var ocorrencia = OcorrenciaAberta(pedido.Id);
        var input = new ResolverOcorrenciaInput(_empresaId, ocorrencia.Id, Guid.NewGuid(), "reembolso", true, 30);
        await FluentActions.Invoking(() => Resolver().ExecuteAsync(input)).Should().ThrowAsync<UnauthorizedAccessException>();
        await _estornos.DidNotReceiveWithAnyArgs().SolicitarAsync(default!, default);
        _estornos.SolicitarAsync(Arg.Any<SolicitarEstornoOnlineInput>(), Arg.Any<CancellationToken>())
            .Returns(new PedidoEstornoOnline { Id = ocorrencia.Id, Situacao = PedidoEstornoOnline.Pendente });
        var r = await Resolver().ExecuteAsync(input with { NivelSolicitante = NivelAcesso.Admin });
        r!.Reembolso!.Codigo.Should().Be("estorno_pendente");
        ocorrencia.EstaAberta.Should().BeTrue();
        ocorrencia.ReembolsoEm.Should().BeNull();
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!, default, default);
    }

    [Fact]
    public async Task ResolverOcorrencia_InexistenteDevolveNull()
    {
        var r = await Resolver().ExecuteAsync(new ResolverOcorrenciaInput(
            _empresaId, Guid.NewGuid(), Guid.NewGuid(), "x", false, null, NivelAcesso.Admin));

        r.Should().BeNull();
    }

    [Fact]
    public async Task Apurar_e_encerrar_preservam_autoria_e_repeticao_nao_duplica_nota()
    {
        var o = OcorrenciaAberta(NovoPedido().Id);
        var usuario = Guid.NewGuid();
        var apurar = new ApurarOcorrenciaUseCase(_repo, _uow, new RelogioFixo(Agora));
        await apurar.ExecuteAsync(_empresaId, o.Id, usuario, "Gerente", NivelAcesso.Gerente);
        await apurar.ExecuteAsync(_empresaId, o.Id, Guid.NewGuid(), "Outro", NivelAcesso.Admin);
        o.ApuradaPorUsuarioId.Should().Be(usuario);
        o.ApuradaPorNome.Should().Be("Gerente");
        var input = new ResolverOcorrenciaInput(_empresaId, o.Id, usuario, new string('x', 1000), false, null, NivelAcesso.Gerente, "Gerente");
        await Resolver().ExecuteAsync(input);
        await Resolver().ExecuteAsync(input with { UsuarioNome = "Outro" });
        o.ResolvidaPorNome.Should().Be("Gerente");
        o.Resolucao.Should().HaveLength(1000);
        await _crm.Received(1).AdicionarNotaAsync(Arg.Is<ClienteNota>(n => n.Texto.Length == 500 && n.Autor == "Gerente"), Arg.Any<CancellationToken>());
        await FluentActions.Invoking(() => Resolver().ExecuteAsync(input with { Resolucao = "outra" }))
            .Should().ThrowAsync<CobrancaPedidoConflitoException>();
    }

    [Fact]
    public async Task Operador_nao_apura_nem_encerra_e_empresa_alheia_nao_altera()
    {
        var o = OcorrenciaAberta(NovoPedido().Id);
        var apurar = new ApurarOcorrenciaUseCase(_repo, _uow, new RelogioFixo(Agora));
        await FluentActions.Invoking(() => apurar.ExecuteAsync(_empresaId, o.Id, Guid.NewGuid(), "Atendimento", NivelAcesso.Operador))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        var input = new ResolverOcorrenciaInput(_empresaId, o.Id, Guid.NewGuid(), "Resolvido", false, null);
        await FluentActions.Invoking(() => Resolver().ExecuteAsync(input)).Should().ThrowAsync<UnauthorizedAccessException>();
        var outra = Guid.NewGuid();
        _repo.ObterTravadaAsync(outra, o.Id, Arg.Any<CancellationToken>()).Returns(o);
        (await Resolver().ExecuteAsync(input with { EmpresaId = outra, NivelSolicitante = NivelAcesso.Admin })).Should().BeNull();
        (await apurar.ExecuteAsync(outra, o.Id, input.UsuarioId, "Dona", NivelAcesso.Admin)).Should().BeNull();
        o.ApuradaEm.Should().BeNull();
        o.EstaAberta.Should().BeTrue();
    }

    [Theory]
    [InlineData("pendente", false)]
    [InlineData("confirmado", false)]
    [InlineData("recusado", true)]
    public async Task Reembolso_legado_impede_encerramento_sem_conferir_exceto_recusa(string situacao, bool permite)
    {
        var o = OcorrenciaAberta(NovoPedido().Id);
        _estornos.ConsultarAsync(_empresaId, o.PedidoId, Arg.Any<CancellationToken>()).Returns(new EstornosOnlineResult([], [
            new PedidoEstornoOnline { Id = o.Id, EmpresaId = _empresaId, PedidoId = o.PedidoId, Situacao = situacao }]));
        var input = new ResolverOcorrenciaInput(_empresaId, o.Id, Guid.NewGuid(), "Resolvido", false, null, NivelAcesso.Admin);
        if (permite) (await Resolver().ExecuteAsync(input))!.Ocorrencia.Status.Should().Be("resolvida");
        else await FluentActions.Invoking(() => Resolver().ExecuteAsync(input)).Should().ThrowAsync<CobrancaPedidoConflitoException>();
    }
}
