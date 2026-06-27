using EasyStock.Api.Configuration;
using EasyStock.Api.Observability;
using EasyStock.Application.Ports.Output.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenTelemetry.Metrics;

namespace EasyStock.Api.UnitTests.Observability;

/// <summary>
/// Prova o WIRING de export, não apenas a chamada do método: sobe o MeterProvider REAL
/// montado por <c>AddEasyStockObservability</c> (que precisa do <c>.AddMeter(MetricNames.MeterName)</c>)
/// e observa o instrumento via InMemoryExporter. Se alguém remover o AddMeter, o counter para
/// de exportar e este teste falha (um mock <c>.Received(1)</c> não pegaria isso).
/// </summary>
public class ObservabilityMetricsWiringTests
{
    private static ServiceProvider BuildProvider(out List<Metric> exported)
    {
        var exportedItems = new List<Metric>();
        exported = exportedItems;

        var services = new ServiceCollection();
        services.AddMetrics();   // garante IMeterFactory (idempotente)
        services.AddLogging();
        var config = new ConfigurationBuilder().Build();
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Production");   // evita ConsoleExporter de dev

        services.AddEasyStockObservability(config, env);
        services.ConfigureOpenTelemetryMeterProvider(b => b.AddInMemoryExporter(exportedItems));

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Counter_de_falhas_exporta_pelo_MeterProvider_real()
    {
        using var sp = BuildProvider(out var exported);
        var meterProvider = sp.GetRequiredService<MeterProvider>();
        var metrics = sp.GetRequiredService<IOperationalMetrics>();

        metrics.IncrementFalhasOperacao("INTERNAL_ERROR");
        meterProvider.ForceFlush(5000);

        var metric = exported.SingleOrDefault(m => m.Name == MetricNames.FalhasOperacaoTotal);
        metric.Should().NotBeNull(
            "o instrumento só é exportado se o meter '{0}' estiver inscrito via .AddMeter(...)",
            MetricNames.MeterName);

        long total = 0;
        foreach (ref readonly var point in metric!.GetMetricPoints())
            total += point.GetSumLong();

        total.Should().Be(1);
    }

    [Fact]
    public void DI_resolve_IOperationalMetrics_nao_nulo()
    {
        // C7: injeção obrigatória só protege se o container de fato registrar a porta.
        using var sp = BuildProvider(out _);

        var metrics = sp.GetService<IOperationalMetrics>();
        metrics.Should().NotBeNull().And.BeOfType<MetricsService>();
    }
}
