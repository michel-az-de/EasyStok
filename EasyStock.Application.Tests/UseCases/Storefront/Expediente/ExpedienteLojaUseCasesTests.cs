using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Expediente;
using EasyStock.Domain.Entities.Storefront;
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

    [Fact]
    public async Task DefinirControle_ForcarFechada_GravaQuemFechouEPublicaEvento()
    {
        var usuario = Guid.NewGuid();
        var useCase = new DefinirControleExpedienteUseCase(_repo, _uow, _publisher, _relogio);

        var resultado = await useCase.ExecuteAsync(new DefinirControleExpedienteCommand(_empresaId, ControleManualLoja.ForcarFechada, usuario));

        resultado.EstaAberta.Should().BeFalse();
        resultado.ControleAlteradoPorUsuarioId.Should().Be(usuario);
        await _repo.Received(1).AddAsync(Arg.Is<ExpedienteLoja>(e => e.ControleManual == ControleManualLoja.ForcarFechada), Arg.Any<CancellationToken>());
        _uow.CommitCount.Should().Be(1);
        await _publisher.Received(1).PublicarAsync("expediente.alterado", _empresaId, Arg.Any<object>(), Arg.Any<CancellationToken>());
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
