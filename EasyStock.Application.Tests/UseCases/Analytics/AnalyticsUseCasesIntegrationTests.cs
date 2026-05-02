using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Alertas;
using EasyStock.Application.UseCases.Analytics.Margem;
using EasyStock.Application.UseCases.Analytics.Movimentacoes;
using EasyStock.Application.UseCases.Analytics.Parados;
using EasyStock.Application.UseCases.Analytics.Receita;
using EasyStock.Application.UseCases.Analytics.Validade;
using EasyStock.Application.UseCases.Analytics.VendasPorCanal;
using EasyStock.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class AnalyticsUseCasesIntegrationTests
{
    private readonly IAnalyticsRepository _repository = Substitute.For<IAnalyticsRepository>();

    [Fact]
    public async Task ObterAlertasUseCase_WithValidCommand_ReturnsAlerts()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var alertas = new List<ValidadeAlerta>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto Vencido", "SKU001", 10, DateTime.UtcNow.AddDays(2), 2, 1000m)
        };

        _repository.GetAlertasValidadeAsync(empresaId, 30, 1, 20, null).Returns((alertas, 1));

        var logger = Substitute.For<ILogger<ObterAlertasUseCase>>();
        var useCase = new ObterAlertasUseCase(_repository, logger);
        var cmd = new ObterAlertasCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().HaveCount(1);
        total.Should().Be(1);
    }

    [Fact]
    public async Task CalcularReceitaUseCase_WithValidCommand_ReturnsRevenue()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var receitas = new List<ReceitaPorPeriodo>
        {
            new(2025, 1, 50000m, 100, 500, 500m)
        };

        _repository.GetReceitaPorPeriodoAsync(empresaId, 12, null).Returns(receitas);

        var logger = Substitute.For<ILogger<CalcularReceitaUseCase>>();
        var useCase = new CalcularReceitaUseCase(_repository, logger);
        var cmd = new CalcularReceitaCommand(empresaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(1);
        result.First().ReceitaBruta.Should().Be(50000m);
    }

    [Fact]
    public async Task CalcularMargemUseCase_WithValidCommand_ReturnsMargin()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var margens = new List<MargemPorProduto>
        {
            new(Guid.NewGuid(), "Produto A", 100m, 150m, 50m, 33.33m, 100)
        };

        _repository.GetMargemPorProdutoAsync(empresaId, 30, 1, 20, null).Returns(margens);

        var logger = Substitute.For<ILogger<CalcularMargemUseCase>>();
        var useCase = new CalcularMargemUseCase(_repository, logger);
        var cmd = new CalcularMargemCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().HaveCount(1);
        items.First().MargemPercentual.Should().BeApproximately(33.33m, 0.01m);
    }

    [Fact]
    public async Task ObterMovimentacoesUseCase_CalculatesCorrectDateRange()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var movimentacoes = new List<MovimentacaoResumo>
        {
            new(2025, 1, 15, TipoMovimentacaoEstoque.Entrada, 10, 100, 5000m)
        };

        var today = DateTime.UtcNow;
        var thirtyDaysAgo = today.AddDays(-30);

        _repository.GetMovimentacoesResumoAsync(Arg.Any<Guid>(), Arg.Is<DateTime>(d => d.Date == thirtyDaysAgo.Date), Arg.Is<DateTime>(d => d.Date >= today.Date.AddDays(-1)), null, null).Returns(movimentacoes);

        var logger = Substitute.For<ILogger<ObterMovimentacoesUseCase>>();
        var useCase = new ObterMovimentacoesUseCase(_repository, logger);
        var cmd = new ObterMovimentacoesCommand(empresaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(1);
    }

    [Fact]
    public async Task ObterValidadeUseCase_WithValidCommand_ReturnsValidityAlerts()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var validadeAlertas = new List<ValidadeAlerta>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto com Vencimento", "SKU001", 5, DateTime.UtcNow.AddDays(5), 5, 500m)
        };

        _repository.GetAlertasValidadeAsync(empresaId, 30, 1, 20, null).Returns((validadeAlertas, 1));

        var logger = Substitute.For<ILogger<ObterValidadeUseCase>>();
        var useCase = new ObterValidadeUseCase(_repository, logger);
        var cmd = new ObterValidadeCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().HaveCount(1);
        items.First().DiasAteVencimento.Should().Be(5);
    }

    [Fact]
    public async Task ObterParadosUseCase_WithValidCommand_ReturnsIdleItems()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var parados = new List<ItemParadoDetalhe>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Produto Parado", "SKU001", 50, DateTime.UtcNow.AddDays(-180), 180, 10000m)
        };

        _repository.GetItensParadosDetalhadosAsync(empresaId, 90, 1, 20, null).Returns((parados, 1));

        var logger = Substitute.For<ILogger<ObterParadosUseCase>>();
        var useCase = new ObterParadosUseCase(_repository, logger);
        var cmd = new ObterParadosCommand(empresaId);

        // Act
        var (items, total) = await useCase.ExecuteAsync(cmd);

        // Assert
        items.Should().HaveCount(1);
        items.First().DiasSemMovimentacao.Should().Be(180);
    }

    [Fact]
    public async Task ObterVendasPorCanalUseCase_WithValidCommand_ReturnsSalesByChannel()
    {
        // Arrange
        var empresaId = Guid.NewGuid();
        var vendas = new List<VendaPorCanal>
        {
            new(CanalVenda.LojaPropria, 100, 500, 50000m, 500m, 70m),
            new(CanalVenda.MercadoLivre, 50, 250, 25000m, 500m, 30m)
        };

        _repository.GetVendasPorCanalAsync(empresaId, Arg.Any<DateTime>(), Arg.Any<DateTime>(), null).Returns(vendas);

        var logger = Substitute.For<ILogger<ObterVendasPorCanalUseCase>>();
        var useCase = new ObterVendasPorCanalUseCase(_repository, logger);
        var cmd = new ObterVendasPorCanalCommand(empresaId);

        // Act
        var result = await useCase.ExecuteAsync(cmd);

        // Assert
        result.Should().HaveCount(2);
        result.First().Canal.Should().Be(CanalVenda.LojaPropria);
    }
}
