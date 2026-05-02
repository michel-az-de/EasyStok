using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;
using EasyStock.Domain.Entities;
using EasyStock.Domain.ValueObjects;
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
            .Returns(Task.FromResult((Enumerable.Empty<ItemEstoque>(), 0)));

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
            .Returns(Task.FromResult((Enumerable.Empty<ItemEstoque>(), 0)));

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
        _configuracaoRepository.GetByLojaIdAsync(lojaId)
            .Returns(Task.FromResult((ConfiguracaoLoja?)null));
        _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, 50, 1, 20, lojaId)
            .Returns(Task.FromResult((Enumerable.Empty<ItemEstoque>(), 0)));

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
            .Returns(Task.FromResult((Enumerable.Empty<ItemEstoque>(), 150)));

        var useCase = new ObterSugestaoReposicaoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterSugestaoReposicaoCommand(empresaId, null, null, 2, 50);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        total.Should().Be(150);
        await _itemEstoqueRepository.Received(1).GetSugestaoReposicaoAsync(empresaId, 50, 2, 50, null);
    }

}
