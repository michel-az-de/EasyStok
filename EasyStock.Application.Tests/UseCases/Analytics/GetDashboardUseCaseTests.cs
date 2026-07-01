using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Dashboard;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class GetDashboardUseCaseTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();
    private readonly IConfiguracaoLojaRepository _config = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly ILogger<GetDashboardUseCase> _logger = Substitute.For<ILogger<GetDashboardUseCase>>();

    // AlertasEstoqueBaixo agora vem da fonte unica (ADR-0039): o use case monta um
    // ObterReposicaoUseCase real com portas mockadas e sobrescreve a contagem do resumo.
    private GetDashboardUseCase CriarUseCase()
    {
        var reposicao = new ObterReposicaoUseCase(_repository, _config, Substitute.For<ILogger<ObterReposicaoUseCase>>());
        return new GetDashboardUseCase(_repository, reposicao, _logger);
    }

    private void MockReposicao(Guid empresaId, params ProdutoReposicaoSnapshot[] snapshot) =>
        _repository.GetSnapshotReposicaoAsync(empresaId, Arg.Any<Guid?>(),
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(snapshot.ToList());

    private static ProdutoReposicaoSnapshot Critico(Guid empresaId) =>
        new(Guid.NewGuid(), null, "Critico", 1m, 5, 2, null, null, null, null, 0m, 0, 7, 1, null, null, 0m);

    [Fact]
    public async Task ExecuteAsync_SobrescreveAlertasEstoqueBaixo_ComContagemDaFonteUnica()
    {
        // O resumo trazia 5 (por-lote); a fonte unica conta {ESGOTADO,CRITICO} = 1 produto critico.
        var empresaId = Guid.NewGuid();
        _repository.GetDashboardResumoAsync(empresaId, 30, null).Returns(
            new DashboardResumo(empresaId, 30, 100, 5000, 50000m, 30000m, 150m, 4500m, 22500m, 5, 3, 2));
        MockReposicao(empresaId, Critico(empresaId));

        var result = await CriarUseCase().ExecuteAsync(new GetDashboardCommand(empresaId));

        result.TotalSkus.Should().Be(100);
        result.MediaVendasDiaria.Should().Be(150m);
        result.AlertasEstoqueBaixo.Should().Be(1, "o card passa a contar produtos {ESGOTADO,CRITICO}, nao lotes");
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomPeriod_UsesProvidedPeriod()
    {
        var empresaId = Guid.NewGuid();
        _repository.GetDashboardResumoAsync(empresaId, 7, null).Returns(
            new DashboardResumo(empresaId, 7, 100, 5000, 50000m, 30000m, 150m, 1050m, 5250m, 5, 3, 2));
        MockReposicao(empresaId);

        var result = await CriarUseCase().ExecuteAsync(new GetDashboardCommand(empresaId, 7));

        result.Periodo.Should().Be(7);
        await _repository.Received(1).GetDashboardResumoAsync(empresaId, 7, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaId_PassesLojaIdToRepository()
    {
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _repository.GetDashboardResumoAsync(empresaId, 30, lojaId).Returns(
            new DashboardResumo(empresaId, 30, 50, 2000, 25000m, 15000m, 75m, 2250m, 11250m, 2, 1, 1));
        MockReposicao(empresaId);

        var result = await CriarUseCase().ExecuteAsync(new GetDashboardCommand(empresaId, 30, lojaId));

        result.TotalSkus.Should().Be(50);
        await _repository.Received(1).GetDashboardResumoAsync(empresaId, 30, lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyEmpresaId_ThrowsValidationException()
    {
        var act = () => CriarUseCase().ExecuteAsync(new GetDashboardCommand(Guid.Empty));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }

    [Fact]
    public async Task ExecuteAsync_SemProdutosCriticos_RetornaZero()
    {
        var empresaId = Guid.NewGuid();
        _repository.GetDashboardResumoAsync(empresaId, 30, null).Returns(
            new DashboardResumo(empresaId, 30, 100, 5000, 50000m, 30000m, 150m, 4500m, 22500m, 9, 0, 0));
        MockReposicao(empresaId); // fonte unica vazia -> 0

        var result = await CriarUseCase().ExecuteAsync(new GetDashboardCommand(empresaId));

        result.AlertasEstoqueBaixo.Should().Be(0, "sem elegiveis na fonte unica, ignora o 9 do resumo");
    }
}
