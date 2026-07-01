using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Analytics.Reposicao;
using EasyStock.Domain.Reposicao;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.Tests.UseCases.Analytics;

public class CalcularReposicaoUseCaseTests
{
    private readonly IAnalyticsRepository _analytics = Substitute.For<IAnalyticsRepository>();
    private readonly IConfiguracaoLojaRepository _config = Substitute.For<IConfiguracaoLojaRepository>();

    // Fonte única: CalcularReposicaoUseCase agora orquestra o ObterReposicaoUseCase (concreto),
    // montado com portas mockadas; o snapshot da IAnalyticsRepository dirige o resultado.
    private CalcularReposicaoUseCase CriarUseCase()
    {
        var reposicao = new ObterReposicaoUseCase(_analytics, _config, Substitute.For<ILogger<ObterReposicaoUseCase>>());
        return new CalcularReposicaoUseCase(reposicao, Substitute.For<ILogger<CalcularReposicaoUseCase>>());
    }

    [Fact]
    public async Task ExecuteAsync_WithValidCommand_ReturnsReplenishmentSuggestions()
    {
        var empresaId = Guid.NewGuid();
        // Produto vigente 3, minimo 5, critico 2 -> ATENCAO (elegivel), custo 10.
        _analytics.GetSnapshotReposicaoAsync(empresaId, null,
                Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(new List<ProdutoReposicaoSnapshot>
            {
                new(Guid.NewGuid(), null, "Produto A", 3m, 5, 2, null, null, null, null, 0m, 0, 7, 1, null, null, 10m)
            });

        var (items, total) = await CriarUseCase().ExecuteAsync(new CalcularReposicaoCommand(empresaId));

        items.Should().HaveCount(1);
        total.Should().Be(1);
        items.First().QuantidadeAtual.Should().Be(3);
        items.First().QuantidadeMinima.Should().Be(5);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyEmpresaId_ThrowsValidationException()
    {
        var act = () => CriarUseCase().ExecuteAsync(new CalcularReposicaoCommand(Guid.Empty));
        await act.Should().ThrowAsync<UseCaseValidationException>();
    }
}
