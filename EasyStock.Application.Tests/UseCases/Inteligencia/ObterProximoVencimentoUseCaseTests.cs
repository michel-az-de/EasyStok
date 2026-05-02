using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class ObterProximoVencimentoUseCaseTests
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly IConfiguracaoLojaRepository _configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly IEasyStockConfiguracoes _config = Substitute.For<IEasyStockConfiguracoes>();
    private readonly ILogger<ObterProximoVencimentoUseCase> _logger = Substitute.For<ILogger<ObterProximoVencimentoUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsProximoVencimento()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _config.DiasAlertaVencimento.Returns(15);
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, 15, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterProximoVencimentoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterProximoVencimentoCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomDias_UsesProvidedDias()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var dias = 30;
        _config.DiasAlertaVencimento.Returns(15);
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, dias, 1, 20, null)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterProximoVencimentoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterProximoVencimentoCommand(empresaId, null, dias, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _itemEstoqueRepository.Received(1).GetProximoVencimentoAsync(empresaId, dias, 1, 20, null);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaId_FetchesConfiguration()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var lojaId = Guid.NewGuid();
        _config.DiasAlertaVencimento.Returns(15);
        _configuracaoRepository.GetByLojaIdAsync(lojaId).Returns((dynamic?)null);
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, 15, 1, 20, lojaId)
            .Returns((Enumerable.Empty<dynamic>(), 0));

        var useCase = new ObterProximoVencimentoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterProximoVencimentoCommand(empresaId, lojaId, null, 1, 20);

        // Act
        await useCase.ExecuteAsync(cmd);

        // Assert
        await _configuracaoRepository.Received(1).GetByLojaIdAsync(lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new ObterProximoVencimentoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterProximoVencimentoCommand(Guid.Empty, null, null, 1, 20);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesDaysCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var today = DateTime.UtcNow;
        var futureDate = today.AddDays(5);

        var mockProduct = Substitute.For<dynamic>();
        mockProduct.Nome = "Produto";

        var items = new[]
        {
            new
            {
                Id = Guid.NewGuid(),
                ProdutoId = Guid.NewGuid(),
                CodigoInterno = "SKU001",
                QuantidadeAtual = (decimal?)100,
                DataVencimento = futureDate,
                Produto = mockProduct
            }
        };

        _config.DiasAlertaVencimento.Returns(15);
        _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, 15, 1, 20, null)
            .Returns((items.AsEnumerable(), 1));

        var useCase = new ObterProximoVencimentoUseCase(_itemEstoqueRepository, _configuracaoRepository, _config, _logger);
        var cmd = new ObterProximoVencimentoCommand(empresaId, null, null, 1, 20);

        // Act
        var (result, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(1);
        result.First().DiasAteVencimento.Should().Be(5);
    }
}
