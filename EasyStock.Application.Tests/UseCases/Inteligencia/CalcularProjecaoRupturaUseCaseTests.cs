using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using static NSubstitute.Arg;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class CalcularProjecaoRupturaUseCaseTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly ILogger<CalcularProjecaoRupturaUseCase> _logger = Substitute.For<ILogger<CalcularProjecaoRupturaUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsProjectionsOrderedByDaysToRupture()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId1 = Guid.NewGuid();
        var produtoId2 = Guid.NewGuid();
        var itemId1 = Guid.NewGuid();
        var itemId2 = Guid.NewGuid();

        var produto1 = new Produto { Id = produtoId1, Nome = "Produto A", EmpresaId = empresaId };
        var produto2 = new Produto { Id = produtoId2, Nome = "Produto B", EmpresaId = empresaId };

        var items = new List<ItemEstoque>
        {
            new() { Id = itemId1, EmpresaId = empresaId, ProdutoId = produtoId1, CodigoInterno = "SKU001", QuantidadeAtual = Quantidade.From(100), Produto = produto1 },
            new() { Id = itemId2, EmpresaId = empresaId, ProdutoId = produtoId2, CodigoInterno = "SKU002", QuantidadeAtual = Quantidade.From(50), Produto = produto2 }
        };

        var taxas = new Dictionary<Guid, decimal>
        {
            { produtoId1, 10m },  // 100 / 10 = 10 dias
            { produtoId2, 10m }   // 50 / 10 = 5 dias
        };

        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, null)
            .Returns(Task.FromResult((items.AsEnumerable(), 2)));

        _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, Any<IEnumerable<Guid>>(), Any<DateTime>(), Any<DateTime>())
            .Returns(Task.FromResult((IReadOnlyDictionary<Guid, decimal>)taxas));

        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(empresaId, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(2);
        total.Should().Be(2);
        result.First().DiasAteRuptura.Should().Be(5);  // Item com menos dias primeiro (50/10=5)
        result.All(r => r.TaxaSaidaDiaria >= 0).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroTaxaDiaria_ReturnsNullDaysToRupture()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var produto = new Produto { Id = produtoId, Nome = "Produto Parado", EmpresaId = empresaId };

        var items = new List<ItemEstoque>
        {
            new() { Id = itemId, EmpresaId = empresaId, ProdutoId = produtoId, CodigoInterno = "SKU001", QuantidadeAtual = Quantidade.From(100), Produto = produto }
        };

        var taxas = new Dictionary<Guid, decimal>();

        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, null)
            .Returns(Task.FromResult((items.AsEnumerable(), 1)));

        _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, Any<IEnumerable<Guid>>(), Any<DateTime>(), Any<DateTime>())
            .Returns(Task.FromResult((IReadOnlyDictionary<Guid, decimal>)taxas));

        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(empresaId, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.First().DiasAteRuptura.Should().BeNull();
        result.First().DataEstimadaRuptura.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithPagination_HandlesCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var items = Enumerable.Range(1, 5).Select(i =>
        {
            var produtoId = Guid.NewGuid();
            var produto = new Produto { Id = produtoId, Nome = "Produto", EmpresaId = empresaId };
            return new ItemEstoque { Id = Guid.NewGuid(), EmpresaId = empresaId, ProdutoId = produtoId, CodigoInterno = $"SKU{i:000}", QuantidadeAtual = Quantidade.From(100), Produto = produto };
        }).ToList();

        var taxas = items.ToDictionary(i => i.ProdutoId, i => 5m);

        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 2, 5, null)
            .Returns(Task.FromResult((items.AsEnumerable(), 25)));

        _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, Any<IEnumerable<Guid>>(), Any<DateTime>(), Any<DateTime>())
            .Returns(Task.FromResult((IReadOnlyDictionary<Guid, decimal>)taxas));

        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(empresaId, 2, 5);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(5);
        total.Should().Be(25);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(Guid.Empty, 1, 20);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyResults_ReturnsEmpty()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20, null)
            .Returns(Task.FromResult((Enumerable.Empty<ItemEstoque>(), 0)));

        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(empresaId, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
        total.Should().Be(0);
    }
}
