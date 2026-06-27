using System.Diagnostics.Metrics;
using EasyStock.Application.Ports.Output.Observability;

namespace EasyStock.Api.Observability;

/// <summary>
/// Adapter de <see cref="IOperationalMetrics"/> sobre <c>System.Diagnostics.Metrics</c>
/// (exportado via OpenTelemetry/OTLP). O meter precisa estar inscrito com
/// <c>.AddMeter(MetricNames.MeterName)</c> no MeterProvider, senão os instrumentos não
/// são coletados (ver <see cref="MetricNames"/>).
/// </summary>
public sealed class MetricsService : IOperationalMetrics
{
    private readonly Counter<long> _falhasOperacaoCounter;

    public MetricsService(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricNames.MeterName);
        _falhasOperacaoCounter = meter.CreateCounter<long>(
            MetricNames.FalhasOperacaoTotal,
            description: "Total de falhas de operação (respostas 5xx tratadas pelo GlobalExceptionHandler)");
    }

    public void IncrementFalhasOperacao(string code) =>
        _falhasOperacaoCounter.Add(1, new KeyValuePair<string, object?>("code", code));
}
