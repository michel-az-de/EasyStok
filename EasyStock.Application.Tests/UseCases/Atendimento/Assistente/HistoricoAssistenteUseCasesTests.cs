using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Assistente;
using EasyStock.Domain.Enums.Atendimento;
using Microsoft.Extensions.Time.Testing;

namespace EasyStock.Application.Tests.UseCases.Atendimento.Assistente;

/// <summary>S67 (#2534): histórico e consumo do Assistente pelo registro dos turnos.</summary>
public class HistoricoAssistenteUseCasesTests
{
    private readonly Guid _empresaId = Guid.NewGuid();
    private readonly ITurnoAgenteRepository _turnos = Substitute.For<ITurnoAgenteRepository>();

    [Fact]
    public async Task UsoSemMesEhOMesCorrenteDeBrasilia()
    {
        // 01/11 às 02h UTC ainda é 31/10 às 23h em Brasília: o consumo é de outubro.
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 2, 0, 0, TimeSpan.Zero));
        var (de, ate) = HorarioBrasil.JanelaMesUtc(new DateOnly(2026, 10, 1));
        _turnos.SomarAsync(_empresaId, de, ate, Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(3, 1_000, 200, 500, 0.01m, 1));

        var uso = await new ObterUsoAssistenteUseCase(_turnos, relogio).ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId));

        uso.Should().Be(new UsoAssistenteResult(2026, 10, 3, 1_000, 200, 500, 1_700, 0.01m, 1));
    }

    [Fact]
    public async Task UsoDeMesInvalidoEhRecusado()
    {
        var act = () => new ObterUsoAssistenteUseCase(_turnos, TimeProvider.System)
            .ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId, 2026, 13));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task HistoricoLimitaOTamanhoDaPaginaEPassaOsFiltros()
    {
        var conversaId = Guid.NewGuid();

        await new ListarHistoricoAssistenteUseCase(_turnos).ExecuteAsync(
            new ListarHistoricoAssistenteQuery(_empresaId, conversaId, DesfechoTurnoAgente.Escalou, 2, 5_000));

        await _turnos.Received(1).ListarAsync(_empresaId,
            new FiltroTurnosAgente(conversaId, DesfechoTurnoAgente.Escalou, 2, ListarHistoricoAssistenteUseCase.TamanhoPaginaMaximo),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HistoricoComPaginaZeroEhRecusado()
    {
        var act = () => new ListarHistoricoAssistenteUseCase(_turnos).ExecuteAsync(new ListarHistoricoAssistenteQuery(_empresaId, Pagina: 0));

        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
