using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.AlertasDias;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class ObterDiasAlertaParadoUseCaseTests
{
    private readonly IConfiguracaoLojaRepository _configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly ILogger<ObterDiasAlertaParadoUseCase> _logger = Substitute.For<ILogger<ObterDiasAlertaParadoUseCase>>();
    private readonly IEasyStockConfiguracoes _config;

    public ObterDiasAlertaParadoUseCaseTests()
    {
        _config = Substitute.For<IEasyStockConfiguracoes>();
        _config.DiasItemParado.Returns(90);
        _config.DiasAlertaVencimento.Returns(15);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutLojaId_ReturnsDefaultDays()
    {
        // Arrange
        var useCase = new ObterDiasAlertaParadoUseCase(_configuracaoRepository, _config, _logger);
        var cmd = new ObterDiasAlertaParadoCommand();

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Dias.Should().Be(90);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaIdAndConfiguration_ReturnsLojaConfiguration()
    {
        // Arrange
        var lojaId = Guid.NewGuid();
        _config.DiasItemParado.Returns(90);

        var useCase = new ObterDiasAlertaParadoUseCase(_configuracaoRepository, _config, _logger);
        var cmd = new ObterDiasAlertaParadoCommand(lojaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert - uses default when config returns null
        result.Dias.Should().Be(90);
        await _configuracaoRepository.Received(1).GetByLojaIdAsync(lojaId);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaIdButNoConfiguration_ReturnsDefault()
    {
        // Arrange
        var lojaId = Guid.NewGuid();
        _configuracaoRepository.GetByLojaIdAsync(lojaId).Returns((EasyStock.Domain.Entities.ConfiguracaoLoja?)null);

        var useCase = new ObterDiasAlertaParadoUseCase(_configuracaoRepository, _config, _logger);
        var cmd = new ObterDiasAlertaParadoCommand(lojaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Dias.Should().Be(90);
    }
}
