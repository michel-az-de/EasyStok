using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

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

        var mockProduct1 = Substitute.For<dynamic>();
        mockProduct1.Nome = "Produto A";

        var mockProduct2 = Substitute.For<dynamic>();
        mockProduct2.Nome = "Produto B";

        var items = new[]
        {
            new
            {
                Id = itemId1,
                ProdutoId = produtoId1,
                CodigoInterno = "SKU001",
                QuantidadeAtual = (decimal?)100,
                Produto = mockProduct1
            },
            new
            {
                Id = itemId2,
                ProdutoId = produtoId2,
                CodigoInterno = "SKU002",
                QuantidadeAtual = (decimal?)50,
                Produto = mockProduct2
            }
        };

        var taxas = new Dictionary<Guid, decimal>
        {
            { produtoId1, 10m },  // 100 / 10 = 10 dias
            { produtoId2, 5m }    // 50 / 5 = 10 dias
        };

        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20)
            .Returns((items.AsEnumerable(), 2));

        _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(taxas);

        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(empresaId, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(2);
        total.Should().Be(2);
        result.First().DiasAteRuptura.Should().Be(5);  // Item com menos dias primeiro
        result.All(r => r.TaxaSaidaDiaria >= 0).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroTaxaDiaria_ReturnsNullDaysToRupture()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var mockProduct = Substitute.For<dynamic>();
        mockProduct.Nome = "Produto Parado";

        var items = new[]
        {
            new
            {
                Id = itemId,
                ProdutoId = produtoId,
                CodigoInterno = "SKU001",
                QuantidadeAtual = (decimal?)100,
                Produto = mockProduct
            }
        };

        var taxas = new Dictionary<Guid, decimal>(); // Sem taxa para este produto

        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20)
            .Returns((items.AsEnumerable(), 1));

        _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(taxas);

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
        var mockProduct = Substitute.For<dynamic>();
        mockProduct.Nome = "Produto";

        var items = Enumerable.Range(1, 5).Select(i => new
        {
            Id = Guid.NewGuid(),
            ProdutoId = Guid.NewGuid(),
            CodigoInterno = $"SKU{i:000}",
            QuantidadeAtual = (decimal?)100,
            Produto = mockProduct
        }).ToList();

        var taxas = items.ToDictionary(i => i.ProdutoId, i => 5m);

        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 2, 5)
            .Returns((items.AsEnumerable(), 25));

        _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, Arg.Any<IEnumerable<Guid>>(), Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(taxas);

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
        _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, 1, 20)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new CalcularProjecaoRupturaUseCase(_itemEstoqueRepository, _movimentacaoRepository, _logger);
        var cmd = new CalcularProjecaoRupturaCommand(empresaId, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
        total.Should().Be(0);
    }
}
