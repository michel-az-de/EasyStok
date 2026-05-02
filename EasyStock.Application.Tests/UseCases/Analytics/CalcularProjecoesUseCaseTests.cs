using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Projecoes;
using EasyStock.Application.UseCases.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class CalcularProjecoesUseCaseTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();
    private readonly ILogger<CalcularProjecoesUseCase> _logger = Substitute.For<ILogger<CalcularProjecoesUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsPagedProjections()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var projecoes = new List<ProjecaoRuptura>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto A", "SKU001", 100, 10m, 10, DateTime.UtcNow.AddDays(10)),
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto B", "SKU002", 50, 5m, 10, DateTime.UtcNow.AddDays(10))
        };

        _repository.GetProjecaoRupturaAsync(empresaId, 30, 1, 20, null).Returns((projecoes, 2));

        var useCase = new CalcularProjecoesUseCase(_repository, _logger);
        var cmd = new CalcularProjecoesCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().HaveCount(2);
        total.Should().Be(2);
        items.First().NomeProduto.Should().Be("Produto A");
        await _repository.Received(1).GetProjecaoRupturaAsync(empresaId, 30, 1, 20, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomPageSize_UsesProvidedPageSize()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _repository.GetProjecaoRupturaAsync(empresaId, 30, 1, 50, null).Returns((new List<ProjecaoRuptura>(), 0));

        var useCase = new CalcularProjecoesUseCase(_repository, _logger);
        var cmd = new CalcularProjecoesCommand(empresaId, Page: 1, PageSize: 50);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _repository.Received(1).GetProjecaoRupturaAsync(empresaId, 30, 1, 50, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyEmpresaId_ThrowsValidationException()
    {
        // Arrange
        var useCase = new CalcularProjecoesUseCase(_repository, _logger);
        var cmd = new CalcularProjecoesCommand(Guid.Empty);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(() => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithNoResults_ReturnsEmptyList()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _repository.GetProjecaoRupturaAsync(empresaId, 30, 1, 20, null).Returns((new List<ProjecaoRuptura>(), 0));

        var useCase = new CalcularProjecoesUseCase(_repository, _logger);
        var cmd = new CalcularProjecoesCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_MapsDtoToResult()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var dataEstimada = DateTime.UtcNow.AddDays(5);
        var projecao = new ProjecaoRuptura(itemId, produtoId, "Produto X", "XSKU", 200, 20m, 5, dataEstimada);

        _repository.GetProjecaoRupturaAsync(empresaId, 30, 1, 20, null).Returns((new[] { projecao }, 1));

        var useCase = new CalcularProjecoesUseCase(_repository, _logger);
        var cmd = new CalcularProjecoesCommand(empresaId);

        // Act
        var (items, _) = await useCase.ExecuteAsync(cmd);
        var result = items.First();

        // Assert
        result.ItemEstoqueId.Should().Be(itemId);
        result.ProdutoId.Should().Be(produtoId);
        result.NomeProduto.Should().Be("Produto X");
        result.TaxaSaidaDiaria.Should().Be(20m);
        result.DiasAteRuptura.Should().Be(5);
        result.DataEstimadaRuptura.Should().Be(dataEstimada);
    }
}
