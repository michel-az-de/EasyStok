using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.ItensParados;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class ObterItensParadosUseCaseTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IEasyStockConfiguracoes _config = Substitute.For<IEasyStockConfiguracoes>();
    private readonly ILogger<ObterItensParadosUseCase> _logger = Substitute.For<ILogger<ObterItensParadosUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsItensParados()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _config.DiasItemParado.Returns(90);
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 1, 20, null)
            .Returns(Task.FromResult((Enumerable.Empty<dynamic>(), 0)));

        var useCase = new ObterItensParadosUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterItensParadosCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomDiasSemMovimento_UsesProvidedDias()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var diasSemMovimento = 180;
        _config.DiasItemParado.Returns(90);
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, diasSemMovimento, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterItensParadosUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterItensParadosCommand(empresaId, null, diasSemMovimento, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _itemEstoqueRepository.Received(1).GetItensParadosAsync(empresaId, diasSemMovimento, 1, 20, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaId_FetchesConfiguration()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _config.DiasItemParado.Returns(90);
        _configuracaoRepository.GetByLojaIdAsync(lojaId).Returns((dynamic?)null);
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 1, 20, lojaId)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterItensParadosUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterItensParadosCommand(empresaId, lojaId, null, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _configuracaoRepository.Received(1).GetByLojaIdAsync(lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new ObterItensParadosUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterItensParadosCommand(Guid.Empty, null, null, 1, 20);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesDaysWithoutMovementCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var today = DateTime.UtcNow;
        var lastMovement = today.AddDays(-180);

        var mockProduct = Substitute.For<dynamic>();
        mockProduct.Nome = "Produto Parado";

        var items = new[]
        {
            new
            {
                Id = Guid.NewGuid(),
                ProdutoId = Guid.NewGuid(),
                CodigoInterno = "SKU001",
                QuantidadeAtual = (decimal?)50,
                UltimaMovimentacao = lastMovement,
                Produto = mockProduct
            }
        };

        _config.DiasItemParado.Returns(90);
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 1, 20, null)
            .Returns((items.AsEnumerable(), 1));

        var useCase = new ObterItensParadosUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterItensParadosCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(1);
        result.First().DiasSemMovimento.Should().Be(180);
    }

    [Fact]
    public async Task ExecuteAsync_WithPagination_PassesCorrectPageParameters()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _config.DiasItemParado.Returns(90);
        _itemEstoqueRepository.GetItensParadosAsync(empresaId, 90, 5, 30, null)
            .Returns((Enumerable.Empty<dynamic>(), 500));

        var useCase = new ObterItensParadosUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterItensParadosCommand(empresaId, null, null, 5, 30);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        total.Should().Be(500);
        await _itemEstoqueRepository.Received(1).GetItensParadosAsync(empresaId, 90, 5, 30, null);
    }
}
