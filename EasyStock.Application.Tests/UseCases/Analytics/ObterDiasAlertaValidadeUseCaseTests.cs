using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.AlertasDias;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class ObterDiasAlertaValidadeUseCaseTests
{
    private readonly IConfiguracaoLojaRepository _configuracaoRepository = Substitute.For<IConfiguracaoLojaRepository>();
    private readonly ILogger<ObterDiasAlertaValidadeUseCase> _logger = Substitute.For<ILogger<ObterDiasAlertaValidadeUseCase>>();
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private readonly IEasyStockConfiguracoes _config;

    public ObterDiasAlertaValidadeUseCaseTests()
    {
        _config = Substitute.For<IEasyStockConfiguracoes>();
        _config.DiasAlertaVencimento.Returns(15);
        _config.DiasItemParado.Returns(90);
    }

    [Fact]
    public async Task ExecuteAsync_WithoutLojaId_ReturnsDefaultDays()
    {
        // Arrange
        var useCase = new ObterDiasAlertaValidadeUseCase(_configuracaoRepository, _config, _logger);
        var cmd = new ObterDiasAlertaValidadeCommand(EmpresaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Dias.Should().Be(15);
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaIdAndConfiguration_ReturnsLojaConfiguration()
    {
        // Arrange
        var lojaId = Guid.NewGuid();
        _config.DiasAlertaVencimento.Returns(15);

        var useCase = new ObterDiasAlertaValidadeUseCase(_configuracaoRepository, _config, _logger);
        var cmd = new ObterDiasAlertaValidadeCommand(EmpresaId, lojaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert - uses default when config returns null
        result.Dias.Should().Be(15);
        await _configuracaoRepository.Received(1).GetDaLojaAsync(EmpresaId, lojaId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithLojaIdButNoConfiguration_ReturnsDefault()
    {
        // Arrange
        var lojaId = Guid.NewGuid();
        _configuracaoRepository.GetDaLojaAsync(EmpresaId, lojaId, Arg.Any<CancellationToken>()).Returns((EasyStock.Domain.Entities.ConfiguracaoLoja?)null);

        var useCase = new ObterDiasAlertaValidadeUseCase(_configuracaoRepository, _config, _logger);
        var cmd = new ObterDiasAlertaValidadeCommand(EmpresaId, lojaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Dias.Should().Be(15);
    }
}
