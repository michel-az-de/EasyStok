using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Inteligencia.Sazonalidade;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Inteligencia;

public class CalcularSazonalidadeInteligenciaUseCaseTests
{
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository = Substitute.For<IMovimentacaoEstoqueRepository>();
    private readonly ILogger<CalcularSazonalidadeInteligenciaUseCase> _logger = Substitute.For<ILogger<CalcularSazonalidadeInteligenciaUseCase>>();

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsMonthlySales()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)> dados = new[]
        {
            (2025, 1, 100, 10000m),
            (2025, 2, 120, 12000m)
        };

        _movimentacaoRepository.GetAgregacaoMensalAsync(empresaId, produtoId, 12)
            .Returns(Task.FromResult(dados));

        var useCase = new CalcularSazonalidadeInteligenciaUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularSazonalidadeInteligenciaCommand(empresaId, produtoId, 12);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(2);
        result.First().Ano.Should().Be(2025);
        result.First().Mes.Should().Be(1);
        result.First().TotalSaidas.Should().Be(100);
        result.First().ValorTotal.Should().Be(10000m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentMonthCount_ReturnsCorrectData()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)> dados = Enumerable.Range(1, 24).Select(m =>
            (2024 + (m - 1) / 12, ((m - 1) % 12) + 1, m * 10, (decimal)(m * 1000m))
        ).ToList();

        _movimentacaoRepository.GetAgregacaoMensalAsync(empresaId, produtoId, 24)
            .Returns(Task.FromResult(dados));

        var useCase = new CalcularSazonalidadeInteligenciaUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularSazonalidadeInteligenciaCommand(empresaId, produtoId, 24);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(24);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyProdutoId_ThrowsValidationException()
    {
        // Arrange
        var useCase = new CalcularSazonalidadeInteligenciaUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularSazonalidadeInteligenciaCommand(Guid.NewGuid(), Guid.Empty, 12);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidEmpresaId_ThrowsException()
    {
        // Arrange
        var useCase = new CalcularSazonalidadeInteligenciaUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularSazonalidadeInteligenciaCommand(Guid.Empty, Guid.NewGuid(), 12);

        // Act & Assert
        await Assert.ThrowsAsync<UseCaseValidationException>(
            () => useCase.ExecuteAsync(cmd));
    }

    [Fact]
    public async Task ExecuteAsync_WithNoData_ReturnsEmpty()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)> dados = Enumerable.Empty<(int, int, int, decimal)>();

        _movimentacaoRepository.GetAgregacaoMensalAsync(empresaId, produtoId, 12)
            .Returns(Task.FromResult(dados));

        var useCase = new CalcularSazonalidadeInteligenciaUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularSazonalidadeInteligenciaCommand(empresaId, produtoId, 12);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_With36Months_ReturnsCorrectly()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var produtoId = Guid.NewGuid();
        IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)> dados = Enumerable.Range(1, 36).Select(m =>
            (2023 + (m - 1) / 12, ((m - 1) % 12) + 1, 100, 10000m)
        ).ToList();

        _movimentacaoRepository.GetAgregacaoMensalAsync(empresaId, produtoId, 36)
            .Returns(Task.FromResult(dados));

        var useCase = new CalcularSazonalidadeInteligenciaUseCase(_movimentacaoRepository, _logger);
        var cmd = new CalcularSazonalidadeInteligenciaCommand(empresaId, produtoId, 36);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(36);
    }
}
