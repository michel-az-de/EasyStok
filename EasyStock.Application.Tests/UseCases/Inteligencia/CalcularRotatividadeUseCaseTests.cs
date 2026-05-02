using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.Rotatividade;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class CalcularRotatividadeUseCaseTests
{
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly ILogger<CalcularRotatividadeUseCase> _logger = Substitute.For<ILogger<CalcularRotatividadeUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsThreeRates()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(10m);

        var useCase = new CalcularRotatividadeUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularRotatividadeCommand(empresaId, null, 30);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.EmpresaId.Should().Be(empresaId);
        result.ProdutoId.Should().BeNull();
        result.PeriodoDias.Should().Be(30);
        result.TaxaSaidaDiaria.Should().Be(10m);
        result.TaxaSaidaSemanal.Should().Be(70m);
        result.TaxaSaidaMensal.Should().Be(300m);
    }

    [Fact]
    public async Task ExecuteAsync_WithProdutoId_ReturnsSpecificProductRates()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(5m);

        var useCase = new CalcularRotatividadeUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularRotatividadeCommand(empresaId, produtoId, 30);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.ProdutoId.Should().Be(produtoId);
        result.TaxaSaidaDiaria.Should().Be(5m);
        result.TaxaSaidaSemanal.Should().Be(35m);
        result.TaxaSaidaMensal.Should().Be(150m);
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroTaxaDiaria_ReturnsAllZeros()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(0m);

        var useCase = new CalcularRotatividadeUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularRotatividadeCommand(empresaId, null, 30);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.TaxaSaidaDiaria.Should().Be(0m);
        result.TaxaSaidaSemanal.Should().Be(0m);
        result.TaxaSaidaMensal.Should().Be(0m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentHistorioPeriod_CalculatesCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(2.5m);

        var useCase = new CalcularRotatividadeUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularRotatividadeCommand(empresaId, null, 15);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.TaxaSaidaDiaria.Should().Be(2.5m);
        result.TaxaSaidaSemanal.Should().Be(17.5m);
        result.TaxaSaidaMensal.Should().Be(75m);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new CalcularRotatividadeUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularRotatividadeCommand(Guid.Empty, null, 30);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithDecimalTaxaDiaria_RoundsCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(3.333333m);

        var useCase = new CalcularRotatividadeUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularRotatividadeCommand(empresaId, null, 30);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.TaxaSaidaDiaria.Should().Be(3.33m);
        result.TaxaSaidaSemanal.Should().Be(23.33m);
        result.TaxaSaidaMensal.Should().Be(100m);
    }
}
