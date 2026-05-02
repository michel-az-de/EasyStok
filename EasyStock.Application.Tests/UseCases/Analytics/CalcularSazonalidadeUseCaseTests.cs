using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Sazonalidade;
using EasyStock.Application.UseCases.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class CalcularSazonalidadeUseCaseTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();
    private readonly ILogger<CalcularSazonalidadeUseCase> _logger = Substitute.For<ILogger<CalcularSazonalidadeUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsMonthlySales()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var sazonalidades = new List<SazonalidadeMensal>
        {
            new(2025, 1, 100, 10000m, 95m),
            new(2025, 2, 120, 12000m, 105m)
        };

        _repository.GetSazonalidadeAsync(empresaId, produtoId, 12, null).Returns(sazonalidades);

        var useCase = new CalcularSazonalidadeUseCase(_repository, _logger);
        var cmd = new CalcularSazonalidadeCommand(empresaId, produtoId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(2);
        result.First().TotalSaidas.Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyProdutoId_ThrowsValidationException()
    {
        // Arrange
        var useCase = new CalcularSazonalidadeUseCase(_repository, _logger);
        var cmd = new CalcularSazonalidadeCommand(Guid.NewGuid(), Guid.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyEmpresaId_ThrowsValidationException()
    {
        // Arrange
        var useCase = new CalcularSazonalidadeUseCase(_repository, _logger);
        var cmd = new CalcularSazonalidadeCommand(Guid.Empty, Guid.NewGuid());

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(cmd));
    }
}
