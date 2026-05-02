using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Application.UseCases.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class CalcularReposicaoUseCaseTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();
    private readonly ILogger<CalcularReposicaoUseCase> _logger = Substitute.For<ILogger<CalcularReposicaoUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsReplenishmentSuggestions()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var reposicoes = new List<ReposicaoSugerida>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto A", "SKU001", 10, 50, 100, 5m, 2, 500m)
        };

        _repository.GetSugestaoReposicaoDetalhadaAsync(empresaId, 30, 1, 20, null).Returns((reposicoes, 1));

        var useCase = new CalcularReposicaoUseCase(_repository, _logger);
        var cmd = new CalcularReposicaoCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().HaveCount(1);
        total.Should().Be(1);
        items.First().QuantidadeSugeridaReposicao.Should().Be(100);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyEmpresaId_ThrowsValidationException()
    {
        // Arrange
        var useCase = new CalcularReposicaoUseCase(_repository, _logger);
        var cmd = new CalcularReposicaoCommand(Guid.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(cmd));
    }
}
