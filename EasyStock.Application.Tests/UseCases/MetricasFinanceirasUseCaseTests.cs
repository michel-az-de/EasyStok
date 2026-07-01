using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Faturas.MetricasFinanceiras;

namespace EasyStock.Application.Tests.UseCases;

/// <summary>
/// Issue 762: a computação migrou para IMetricasFinanceirasQueries (Infra, RLS bypass
/// condicional a SuperAdmin); o use-case mantém clamp da janela + cache TTL 5min.
/// </summary>
public class MetricasFinanceirasUseCaseTests
{
    private readonly IMetricasFinanceirasQueries _queries = Substitute.For<IMetricasFinanceirasQueries>();
    private readonly ICacheService _cache = Substitute.For<ICacheService>();
    private readonly MetricasFinanceirasUseCase _useCase;

    public MetricasFinanceirasUseCaseTests()
    {
        _useCase = new MetricasFinanceirasUseCase(_queries, _cache);
        _queries.ComputarAsync(Arg.Any<int>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci => Resultado(mrr: 149.90m));
    }

    [Theory]
    [InlineData(0, 1)]      // abaixo do piso -> 1
    [InlineData(30, 30)]    // dentro da faixa -> inalterado
    [InlineData(9999, 365)] // acima do teto -> 365
    public async Task Clampa_janela_antes_de_delegar(int pedido, int esperado)
    {
        await _useCase.ExecuteAsync(new MetricasFinanceirasCommand(DiasRetroativo: pedido));

        await _queries.Received(1).ComputarAsync(esperado, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cache_hit_nao_recomputa()
    {
        _cache.GetAsync<MetricasFinanceirasResult>("metricas:all:30")
            .Returns(Resultado(mrr: 300m));

        var result = await _useCase.ExecuteAsync(new MetricasFinanceirasCommand());

        result.Mrr.Should().Be(300m);
        await _queries.DidNotReceiveWithAnyArgs()
            .ComputarAsync(default, default, default);
    }

    [Fact]
    public async Task ForcarRefresh_ignora_cache_e_recomputa()
    {
        _cache.GetAsync<MetricasFinanceirasResult>("metricas:all:30")
            .Returns(Resultado(mrr: 300m)); // cache "sujo" que deve ser ignorado

        var result = await _useCase.ExecuteAsync(new MetricasFinanceirasCommand(ForcarRefresh: true));

        result.Mrr.Should().Be(149.90m); // veio da porta, não do cache
        await _queries.Received(1).ComputarAsync(30, null, Arg.Any<CancellationToken>());
        await _cache.Received(1).SetAsync("metricas:all:30", Arg.Any<MetricasFinanceirasResult>(), Arg.Any<TimeSpan?>());
    }

    private static MetricasFinanceirasResult Resultado(decimal mrr) => new(
        Mrr: mrr, Arr: mrr * 12m,
        AssinaturasAtivas: 1, AssinaturasSuspensas: 0, AssinaturasCanceladas: 0,
        FaturasEmitidasPeriodo: 0, FaturasPagasPeriodo: 0, FaturasVencidas: 0,
        TaxaConversao: 0m, ReceitaPeriodo: 0m, ValorVencido: 0m, TicketMedio: 0m,
        AtrasoMedioDias: 0, TopInadimplentes: Array.Empty<TopInadimplenteResult>(),
        PeriodoInicio: DateTime.UtcNow.AddDays(-30), PeriodoFim: DateTime.UtcNow);
}
