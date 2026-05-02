using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Inteligencia.Board;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class GetBoardUseCaseTests
{
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly IItemEstoqueRepository _itemEstoqueRepository = Substitute.For<IItemEstoqueRepository>();
    private readonly ILogger<GetBoardUseCase> _logger = Substitute.For<ILogger<GetBoardUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsBoard()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult(10m));
        _itemEstoqueRepository.GetResumoEstoqueAsync(empresaId)
            .Returns(Task.FromResult((quantidadeEmEstoque: 1000, valorTotalEstoque: 50000m, ticketMedioSugerido: 150m)));

        var useCase = new GetBoardUseCase(_movimentacaoRepository, _itemEstoqueRepository, _logger);
        var cmd = new GetBoardCommand(empresaId, 30);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.EmpresaId.Should().Be(empresaId);
        result.Periodo.Should().Be(30);
        result.QuantidadeEmEstoque.Should().Be(1000);
        result.ValorTotalEstoque.Should().Be(50000m);
        result.MediaVendasDiaria.Should().Be(10m);
        result.ProjecaoVendasPeriodo.Should().Be(300m);
        result.ProjecaoReceitaPeriodo.Should().Be(45000m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentPeriodo_CalculatesCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult(5m));
        _itemEstoqueRepository.GetResumoEstoqueAsync(empresaId)
            .Returns(Task.FromResult((quantidadeEmEstoque: 500, valorTotalEstoque: 25000m, ticketMedioSugerido: 100m)));

        var useCase = new GetBoardUseCase(_movimentacaoRepository, _itemEstoqueRepository, _logger);
        var cmd = new GetBoardCommand(empresaId, 7);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.ProjecaoVendasPeriodo.Should().Be(35m);
        result.ProjecaoReceitaPeriodo.Should().Be(3500m);
    }

    [Fact]
    public async Task ExecuteAsync_WithZeroTaxaDiaria_ReturnsZeroProjections()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult(0m));
        _itemEstoqueRepository.GetResumoEstoqueAsync(empresaId)
            .Returns(Task.FromResult((quantidadeEmEstoque: 100, valorTotalEstoque: 5000m, ticketMedioSugerido: 50m)));

        var useCase = new GetBoardUseCase(_movimentacaoRepository, _itemEstoqueRepository, _logger);
        var cmd = new GetBoardCommand(empresaId, 30);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.MediaVendasDiaria.Should().Be(0m);
        result.ProjecaoVendasPeriodo.Should().Be(0m);
        result.ProjecaoReceitaPeriodo.Should().Be(0m);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new GetBoardUseCase(_movimentacaoRepository, _itemEstoqueRepository, _logger);
        var cmd = new GetBoardCommand(Guid.Empty, 30);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxPeriodo_CalculatesCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, Arg.Any<DateTime>(), Arg.Any<DateTime>())
            .Returns(Task.FromResult(2m));
        _itemEstoqueRepository.GetResumoEstoqueAsync(empresaId)
            .Returns(Task.FromResult((quantidadeEmEstoque: 1000, valorTotalEstoque: 100000m, ticketMedioSugerido: 200m)));

        var useCase = new GetBoardUseCase(_movimentacaoRepository, _itemEstoqueRepository, _logger);
        var cmd = new GetBoardCommand(empresaId, 365);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.ProjecaoVendasPeriodo.Should().Be(730m);
    }
}
