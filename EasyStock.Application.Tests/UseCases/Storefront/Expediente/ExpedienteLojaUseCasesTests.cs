using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Expediente;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.TestHelpers;

namespace EasyStock.Application.Tests.UseCases.Storefront.Expediente;

/// <summary>S40: expediente da loja pelo console (GET, PUT de horários e mensagens, controle manual).</summary>
public class ExpedienteLojaUseCasesTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly IExpedienteLojaRepository _repo = Substitute.For<IExpedienteLojaRepository>();
    private readonly IOperacaoEventPublisher _publisher = Substitute.For<IOperacaoEventPublisher>();
    private readonly FakeUnitOfWork _uow = new();
    private readonly IAuditLogRepository _auditoria = Substitute.For<IAuditLogRepository>();
    private readonly INotificadorService _notificador = Substitute.For<INotificadorService>();

    // Segunda, 28/09/2026, 12:00 em Brasília (15:00 UTC).
    private readonly FakeTimeProvider _relogio = new(new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Obter_SemRegistro_DevolvePadraoAberto()
    {
        var resultado = await new ObterExpedienteLojaUseCase(_repo, _relogio).ExecuteAsync(_empresaId);

        resultado.ControleManual.Should().Be(ControleManualLoja.Automatico);
        resultado.Horarios.Should().HaveCount(7);
        resultado.EstaAberta.Should().BeTrue();
    }

    private DefinirControleExpedienteUseCase NovoControle(FakeTimeProvider? relogio = null) =>
        new(_repo, _uow, _publisher, relogio ?? _relogio, _auditoria, _notificador);

    // Segunda, 28/09/2026, 23:30 em Brasília (02:30 UTC de terça): fora do turno padrão 08–22 h.
    private static FakeTimeProvider ForaDoHorario() => new(new DateTimeOffset(2026, 9, 29, 2, 30, 0, TimeSpan.Zero));

    [Fact]
    public async Task DefinirControle_FecharNoHorario_GerenteComJustificativa_GravaAuditoriaEAvisaOsDonos()
    {
        var usuario = Guid.NewGuid();

        var resultado = await NovoControle().ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarFechada, usuario, NivelAcesso.Gerente, "  Falta de luz no bairro  "));

        resultado.EstaAberta.Should().BeFalse();
        resultado.ControleAlteradoPorUsuarioId.Should().Be(usuario);
        await _repo.Received(1).AddAsync(Arg.Is<ExpedienteLoja>(e => e.ControleManual == ControleManualLoja.ForcarFechada), Arg.Any<CancellationToken>());
        await _auditoria.Received(1).AddAsync(Arg.Is<AuditLog>(a =>
            a.UsuarioId == usuario
            && a.Acao == DefinirControleExpedienteUseCase.AcaoAuditoriaFecharNoHorario
            && a.Sucesso
            && a.Detalhes!.Contains("Falta de luz no bairro")
            && a.Detalhes.Contains(_empresaId.ToString())));
        await _notificador.Received(1).EnfileirarEventoAsync(
            TipoEventoNotificacao.LojaFechadaNoHorario, _empresaId,
            Arg.Is<string>(p => p.Contains("Falta de luz no bairro") && !p.Contains("\"usuarioId\"")),
            Arg.Any<Guid?>(), Arg.Any<CancellationToken>(), Arg.Any<string?>());
        _uow.CommitCount.Should().Be(1);
        await _publisher.Received(1).PublicarAsync("expediente.alterado", _empresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("porque")]
    public async Task DefinirControle_FecharNoHorario_SemJustificativaSuficiente_Recusa(string? justificativa)
    {
        var act = () => NovoControle().ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarFechada, Guid.NewGuid(), NivelAcesso.Admin, justificativa));

        await act.Should().ThrowAsync<UseCaseValidationException>().WithMessage("*justificativa*");
        _uow.CommitCount.Should().Be(0);
        await _auditoria.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Theory]
    [InlineData(NivelAcesso.Operador)]
    [InlineData(NivelAcesso.Visualizador)]
    public async Task DefinirControle_FecharNoHorario_AbaixoDeGerente_Recusa(NivelAcesso nivel)
    {
        var act = () => NovoControle().ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarFechada, Guid.NewGuid(), nivel, "Falta de luz no bairro"));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task DefinirControle_FecharForaDoHorario_SemJustificativa_SegueSemAuditoria()
    {
        var resultado = await NovoControle(ForaDoHorario()).ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarFechada, Guid.NewGuid(), NivelAcesso.Gerente));

        resultado.EstaAberta.Should().BeFalse();
        _uow.CommitCount.Should().Be(1);
        await _auditoria.DidNotReceiveWithAnyArgs().AddAsync(default!);
        await _notificador.DidNotReceiveWithAnyArgs().EnfileirarEventoAsync(default, default, default!);
    }

    [Fact]
    public async Task DefinirControle_FecharNoHorario_SemUsuario_Recusa()
    {
        var act = () => NovoControle().ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarFechada, null, NivelAcesso.Admin, "Falta de luz no bairro"));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task DefinirControle_VoltarAoHorario_SoAdmin()
    {
        var act = () => NovoControle().ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.Automatico, Guid.NewGuid(), NivelAcesso.Gerente));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task DefinirControle_AbrirNaMao_Operador_Recusa()
    {
        var act = () => NovoControle().ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarAberta, Guid.NewGuid(), NivelAcesso.Operador));

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task DefinirControle_AbrirNaMao_Gerente_Abre()
    {
        var resultado = await NovoControle(ForaDoHorario()).ExecuteAsync(new DefinirControleExpedienteCommand(
            _empresaId, ControleManualLoja.ForcarAberta, Guid.NewGuid(), NivelAcesso.Gerente));

        resultado.EstaAberta.Should().BeTrue();
        _uow.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Atualizar_HorarioInvalido_LancaValidacaoSemGravar()
    {
        var useCase = new AtualizarExpedienteLojaUseCase(_repo, _uow, _publisher, _relogio);

        var act = () => useCase.ExecuteAsync(new AtualizarExpedienteLojaCommand(
            _empresaId, [new HorarioFuncionamento(9, new TimeOnly(8, 0), new TimeOnly(22, 0))], null, null));

        await act.Should().ThrowAsync<UseCaseValidationException>();
        _uow.CommitCount.Should().Be(0);
    }

    [Fact]
    public async Task Atualizar_RegistroExistente_AtualizaHorariosEMensagens()
    {
        var existente = ExpedienteLoja.CriarPadrao(_empresaId);
        _repo.GetByEmpresaIdAsync(_empresaId, Arg.Any<CancellationToken>()).Returns(existente);
        var useCase = new AtualizarExpedienteLojaUseCase(_repo, _uow, _publisher, _relogio);

        var resultado = await useCase.ExecuteAsync(new AtualizarExpedienteLojaCommand(
            _empresaId,
            [new HorarioFuncionamento(1, new TimeOnly(11, 0), new TimeOnly(15, 0))],
            "Fechado. Abrimos {abre}.",
            null));

        resultado.Horarios.Should().ContainSingle().Which.DiaDaSemana.Should().Be(1);
        resultado.MensagemForaDoHorario.Should().Be("Fechado. Abrimos {abre}.");
        resultado.EstaAberta.Should().BeTrue("segunda 12:00 cai no turno das 11 às 15");
        await _repo.Received(1).UpdateAsync(existente, Arg.Any<CancellationToken>());
    }
}
