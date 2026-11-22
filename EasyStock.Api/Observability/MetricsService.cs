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
    private readonly Counter<long> _webhookSemEfeitoCounter;

    public MetricsService(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MetricNames.MeterName);
        _falhasOperacaoCounter = meter.CreateCounter<long>(
            MetricNames.FalhasOperacaoTotal,
            description: "Total de falhas de operação (respostas 5xx tratadas pelo GlobalExceptionHandler)");
        _webhookSemEfeitoCounter = meter.CreateCounter<long>(
            MetricNames.WebhookSemEfeitoTotal,
            description: "Total de webhooks aceitos (200) que não gravaram nada, por motivo");
    }

    public void IncrementFalhasOperacao(string code) =>
        _falhasOperacaoCounter.Add(1, new KeyValuePair<string, object?>("code", code));

    public void IncrementWebhookSemEfeito(string motivo) =>
        _webhookSemEfeitoCounter.Add(1, new KeyValuePair<string, object?>("motivo", motivo));
}
