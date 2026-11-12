using EasyStock.Infra.Async.Logging;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EasyStock.Worker.DependencyInjection;

/// <summary>
/// Telemetria OTLP opt-in (issue 1002): só registra providers/exporters quando
/// <c>OpenTelemetry:OtlpEndpoint</c> está configurado (na VM consolidada, a env
/// <c>OpenTelemetry__OtlpEndpoint</c> aponta pro otel-collector). Sem endpoint
/// (dev local, CI), nada é registrado. Worker não expõe HTTP: os traces vêm de
/// HttpClient (integrações) e Npgsql (jobs de outbox/relatórios); logs vão pelo
/// sink Serilog OTLP configurado no Program.cs.
/// </summary>
public static class TelemetrySetup
{
    public static void AddEasyStockTelemetry(this HostApplicationBuilder builder, string serviceName)
    {
        if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is not { Length: > 0 } endpoint
            || string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        var otlp = new Uri(endpoint);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                // Jobs de polling geram spans Npgsql como raiz o tempo todo; 25%
                // mantém amostra útil sem inundar o OpenObserve (retenção de 30 dias).
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.25)))
                // #2376: o span sai com o mesmo mascaramento do log; antes do exporter.
                .AddProcessor(new SpanQueRedige())
                .AddOtlpExporter(options => options.Endpoint = otlp))
            .WithMetrics(metrics => metrics
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = otlp));
    }
}
