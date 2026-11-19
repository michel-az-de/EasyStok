using EasyStock.Application.Common;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Assistente;
using EasyStock.Domain.Entities.Atendimento;
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

    [Theory]
    [InlineData(1_000L, 790L, 79, false)]
    [InlineData(1_000L, 800L, 80, true)]
    [InlineData(1_000L, 1_500L, 150, true)]
    public async Task UsoComCotaDizPercentualEAlertaAPartirDe80(long cota, long usados, int percentual, bool alerta)
    {
        // S69 (#2853): mesma soma que a etapa da cota usa no turno, para a barra e o bloqueio nunca discordarem.
        var configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
        var configuracao = ConfiguracaoAtendimento.CriarPadrao(_empresaId);
        configuracao.DefinirAssistente(null, null, cota, null, null);
        configuracoes.GetByEmpresaIdAsync(_empresaId).Returns(configuracao);
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(1, usados, 0, 0, 0m, 0));

        var uso = await new ObterUsoAssistenteUseCase(_turnos, TimeProvider.System, configuracoes)
            .ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId));

        uso.CotaTokensMes.Should().Be(cota);
        uso.Percentual.Should().Be(percentual);
        uso.Alerta.Should().Be(alerta);
    }

    [Fact]
    public async Task UsoSemCotaNaoTemPercentualNemAlerta()
    {
        var configuracoes = Substitute.For<IConfiguracaoAtendimentoRepository>();
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(1, 5_000, 0, 0, 0m, 0));

        var uso = await new ObterUsoAssistenteUseCase(_turnos, TimeProvider.System, configuracoes)
            .ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId));

        uso.CotaTokensMes.Should().BeNull();
        uso.Percentual.Should().BeNull();
        uso.Alerta.Should().BeFalse();
    }

    [Fact]
    public async Task UsoDizQuantasRespostasSairamSemIaEAEstimativaPoupada()
    {
        // S78 (#2896): 3 respostas sem IA e 2 turnos do modelo que gastaram 30 mil tokens. Média de 15 mil por turno
        // do modelo: as 3 sem IA pouparam perto de 45 mil.
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(5, 20_000, 4_000, 6_000, 0.05m, 3, RespostasSemIa: 3));

        var uso = await new ObterUsoAssistenteUseCase(_turnos, TimeProvider.System).ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId));

        uso.Turnos.Should().Be(5);
        uso.RespostasSemIa.Should().Be(3);
        uso.TokensPoupadosEstimados.Should().Be(45_000);
    }

    [Fact]
    public async Task UsoSemTurnoDoModeloNaoInventaEstimativa()
    {
        // Sem turno do modelo no mês não há média: a contagem aparece, a estimativa não.
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(2, 0, 0, 0, 0m, 2, RespostasSemIa: 2));

        var uso = await new ObterUsoAssistenteUseCase(_turnos, TimeProvider.System).ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId));

        uso.RespostasSemIa.Should().Be(2);
        uso.TokensPoupadosEstimados.Should().BeNull();
    }

    [Fact]
    public async Task UsoSemRespostaSemIaNaoTemEstimativa()
    {
        _turnos.SomarAsync(_empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(new SomaTurnosAgente(4, 40_000, 0, 0, 0m, 0));

        var uso = await new ObterUsoAssistenteUseCase(_turnos, TimeProvider.System).ExecuteAsync(new ObterUsoAssistenteQuery(_empresaId));

        uso.RespostasSemIa.Should().Be(0);
        uso.TokensPoupadosEstimados.Should().BeNull();
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
