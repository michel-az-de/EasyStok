using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class ObterSugestaoReposicaoUseCaseTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IEasyStockConfiguracoes _config = Substitute.For<IEasyStockConfiguracoes>();
    private readonly ILogger<ObterSugestaoReposicaoUseCase> _logger = Substitute.For<ILogger<ObterSugestaoReposicaoUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsSugestaoReposicao()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 50, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomLimiteQuantidade_UsesProvidedLimit()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var limite = 100;
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, limite, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(empresaId, null, limite, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _itemEstoqueRepository.Received(1).GetSugestaoReposicaoAsync(empresaId, limite, 1, 20, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaId_FetchesConfiguration()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _configuracaoRepository.GetByLojaIdAsync(lojaId).Returns((dynamic?)null);
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 50, 1, 20, lojaId)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(empresaId, lojaId, null, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _configuracaoRepository.Received(1).GetByLojaIdAsync(lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(Guid.Empty, null, null, 1, 20);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithPagination_PassesCorrectPageParameters()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 50, 2, 50, null)
            .Returns((Enumerable.Empty<dynamic>(), 150));

        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(empresaId, null, null, 2, 50);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        total.Should().Be(150);
        await _itemEstoqueRepository.Received(1).GetSugestaoReposicaoAsync(empresaId, 50, 2, 50, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithResults_MapsSugestaoCorretamente()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();

        var mockProduct = Substitute.For<dynamic>();
        mockProduct.Nome = "Produto";

        var items = new[]
        {
            new
            {
                Id = itemId,
                ProdutoId = produtoId,
                CodigoInterno = "SKU001",
                QuantidadeAtual = (decimal?)25,
                QuantidadeSugerida = 75m,
                CustoEstimado = 7500m,
                Produto = mockProduct
            }
        };

        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 50, 1, 20, null)
            .Returns((items.AsEnumerable(), 1));

        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(1);
        result.First().ItemEstoqueId.Should().Be(itemId);
        result.First().QuantidadeSugerida.Should().Be(75m);
        result.First().CustoEstimado.Should().Be(7500m);
        result.First().LimiteMinimo.Should().Be(50);
    }
}
