using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Dashboard;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class GetDashboardUseCaseTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();
    private readonly ILogger<GetDashboardUseCase> _logger = Substitute.For<ILogger<GetDashboardUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsDashboard()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var dashboard = new DashboardResumo(
            empresaId, 30, 100, 5000, 50000m, 30000m, 150m, 4500m, 22500m,
            5, 3, 2);

        _repository.GetDashboardResumoAsync(empresaId, 30, null).Returns(dashboard);

        var useCase = new GetDashboardUseCase(_repository, _logger);
        var cmd = new GetDashboardCommand(empresaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().NotBeNull();
        result.TotalSkus.Should().Be(100);
        result.QuantidadeTotalEmEstoque.Should().Be(5000);
        result.MediaVendasDiaria.Should().Be(150m);
        await _repository.Received(1).GetDashboardResumoAsync(empresaId, 30, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomPeriod_UsesProvidedPeriod()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var dashboard = new DashboardResumo(empresaId, 7, 100, 5000, 50000m, 30000m, 150m, 1050m, 5250m, 5, 3, 2);

        _repository.GetDashboardResumoAsync(empresaId, 7, null).Returns(dashboard);

        var useCase = new GetDashboardUseCase(_repository, _logger);
        var cmd = new GetDashboardCommand(empresaId, 7);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Periodo.Should().Be(7);
        await _repository.Received(1).GetDashboardResumoAsync(empresaId, 7, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaId_PassesLojaIdToRepository()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        var dashboard = new DashboardResumo(empresaId, 30, 50, 2000, 25000m, 15000m, 75m, 2250m, 11250m, 2, 1, 1);

        _repository.GetDashboardResumoAsync(empresaId, 30, lojaId).Returns(dashboard);

        var useCase = new GetDashboardUseCase(_repository, _logger);
        var cmd = new GetDashboardCommand(empresaId, 30, lojaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.TotalSkus.Should().Be(50);
        await _repository.Received(1).GetDashboardResumoAsync(empresaId, 30, lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyEmpresaId_ThrowsValidationException()
    {
        // Arrange
        var useCase = new GetDashboardUseCase(_repository, _logger);
        var cmd = new GetDashboardCommand(Guid.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroAlerts_ReturnsZeroAlerts()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var dashboard = new DashboardResumo(empresaId, 30, 100, 5000, 50000m, 30000m, 150m, 4500m, 22500m, 0, 0, 0);

        _repository.GetDashboardResumoAsync(empresaId, 30, null).Returns(dashboard);

        var useCase = new GetDashboardUseCase(_repository, _logger);
        var cmd = new GetDashboardCommand(empresaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.AlertasEstoqueBaixo.Should().Be(0);
        result.AlertasVencimento.Should().Be(0);
        result.AlertasItensParados.Should().Be(0);
    }
}
