using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class ObterEstoqueBaixoUseCaseTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IEasyStockConfiguracoes _config = Substitute.For<IEasyStockConfiguracoes>();
    private readonly ILogger<ObterEstoqueBaixoUseCase> _logger = Substitute.For<ILogger<ObterEstoqueBaixoUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsEstoqueBaixo()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, 50, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterEstoqueBaixoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterEstoqueBaixoCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomLimit_UsesProvidedLimit()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var limite = 100;
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, limite, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterEstoqueBaixoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterEstoqueBaixoCommand(empresaId, null, limite, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _itemEstoqueRepository.Received(1).GetEstoqueBaixoAsync(empresaId, limite, 1, 20, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaId_FetchesConfiguration()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _config.LimiteEstoqueBaixoDefault.Returns(50);
        _configuracaoRepository.GetByLojaIdAsync(lojaId).Returns((dynamic?)null);
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, 50, 1, 20, lojaId)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterEstoqueBaixoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterEstoqueBaixoCommand(empresaId, lojaId, null, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _configuracaoRepository.Received(1).GetByLojaIdAsync(lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new ObterEstoqueBaixoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterEstoqueBaixoCommand(Guid.Empty, null, null, 1, 20);

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
        _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, 50, 3, 50, null)
            .Returns((Enumerable.Empty<dynamic>(), 200));

        var useCase = new ObterEstoqueBaixoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterEstoqueBaixoCommand(empresaId, null, null, 3, 50);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        total.Should().Be(200);
        await _itemEstoqueRepository.Received(1).GetEstoqueBaixoAsync(empresaId, 50, 3, 50, null);
    }
}
