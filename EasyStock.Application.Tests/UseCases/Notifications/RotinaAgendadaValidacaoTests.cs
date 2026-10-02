using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using Microsoft.Extensions.Logging.Abstractions;

namespace EasyStock.Application.Tests.UseCases.Notifications;

/// <summary>N12: a agenda é <c>ParametrosJson.agenda.horario</c> (HH:mm de Brasília); cron não é mais criável.</summary>
public class RotinaAgendadaValidacaoTests
{
    private readonly IRotinaRepository _rotinas = Substitute.For<IRotinaRepository>();
    private readonly IUnitOfWork _uow = Substitute.For<IUnitOfWork>();
    private readonly Guid _empresaId = Guid.NewGuid();

    private CriarRotinaUseCase Criar() => new(_rotinas, _uow, NullLogger<CriarRotinaUseCase>.Instance);

    private CriarRotinaCommand Comando(TriggerTipoRotina trigger = TriggerTipoRotina.Evento, string? cron = null, string? parametros = null) =>
        new("resumo_diario_empresa", "Resumo", TipoEventoNotificacao.ResumoDiario, trigger, "resumo_diario_email_v1",
            CategoriaConteudoNotificacao.Operacional, cron, parametros, _empresaId);

    [Theory]
    [InlineData("""{"agenda":{"horario":"8:00"}}""")]
    [InlineData("""{"agenda":{"horario":"24:00"}}""")]
    [InlineData("""{"agenda":{"horario":"20:00:00"}}""")]
    [InlineData("""{"agenda":{"horario":""}}""")]
    [InlineData("""{"agenda":{}}""")]
    [InlineData("""{"agenda":"20:00"}""")]
    public async Task HorarioForaDoFormatoRecusa(string parametros)
    {
        var act = () => Criar().ExecuteAsync(Comando(parametros: parametros));

        var ex = await act.Should().ThrowAsync<UseCaseValidationException>();
        ex.Which.Code.Should().Be("AGENDA_HORARIO_INVALIDO");
        await _rotinas.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task HorarioValidoEhAceito()
    {
        var resultado = await Criar().ExecuteAsync(Comando(parametros: """{"agenda":{"horario":"20:00"}}"""));

        resultado.Codigo.Should().Be("resumo_diario_empresa");
        await _rotinas.Received(1).AddAsync(Arg.Any<RotinaNotificacao>());
    }

    [Fact]
    public async Task CronNaoEhCriavel()
    {
        var act = () => Criar().ExecuteAsync(Comando(TriggerTipoRotina.Cron, "0 20 * * *"));

        var ex = await act.Should().ThrowAsync<UseCaseValidationException>();
        ex.Which.Code.Should().Be("CRON_NAO_SUPORTADO");
        ex.Which.Message.Should().Contain("agenda.horario");
        await _rotinas.DidNotReceiveWithAnyArgs().AddAsync(default!);
    }

    [Fact]
    public async Task AtualizarTambemValidaOHorarioERecusaCron()
    {
        var rotina = RotinaNotificacao.Criar("r", "R", TipoEventoNotificacao.ResumoDiario, TriggerTipoRotina.Evento,
            "t", CategoriaConteudoNotificacao.Operacional, empresaId: _empresaId);
        _rotinas.GetByIdAsync(rotina.Id, Arg.Any<CancellationToken>()).Returns(rotina);
        var sut = new AtualizarRotinaUseCase(_rotinas, _uow);

        var ruim = () => sut.ExecuteAsync(new AtualizarRotinaCommand(rotina.Id, null, """{"agenda":{"horario":"99:99"}}""", "u", _empresaId));
        (await ruim.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("AGENDA_HORARIO_INVALIDO");

        var cron = () => sut.ExecuteAsync(new AtualizarRotinaCommand(rotina.Id, "0 20 * * *", null, "u", _empresaId));
        (await cron.Should().ThrowAsync<UseCaseValidationException>()).Which.Code.Should().Be("CRON_NAO_SUPORTADO");

        var bom = () => sut.ExecuteAsync(new AtualizarRotinaCommand(rotina.Id, null, """{"agenda":{"horario":"09:30"}}""", "u", _empresaId));
        await bom.Should().NotThrowAsync();
    }
}
