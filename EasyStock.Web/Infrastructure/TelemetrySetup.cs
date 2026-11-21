using EasyStock.Observabilidade;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace EasyStock.Web.Infrastructure;

/// <summary>
/// Telemetria OTLP opt-in (issue 1002): só registra providers/exporters quando
/// <c>OpenTelemetry:OtlpEndpoint</c> está configurado (na VM consolidada, a env
/// <c>OpenTelemetry__OtlpEndpoint</c> aponta pro otel-collector). Sem endpoint
/// (dev local, CI), nada é registrado — zero overhead e zero ruído de export.
/// Mesma chave de configuração usada pela Api (ConfigurationKeys.OtlpEndpoint).
/// </summary>
public static class TelemetrySetup
{
    public static void AddEasyStockTelemetry(this WebApplicationBuilder builder, string serviceName)
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
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Web/Admin são a raiz dos traces distribuídos; a Api segue a decisão
                // via ParentBasedSampler. 25% é suficiente pro tráfego de homolog.
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(0.25)))
                // #2390: redige caminho, URL e nome do span antes do exporter (a ordem de registro importa).
                .AddProcessor(new SpanQueRedige())
                .AddOtlpExporter(options => options.Endpoint = otlp))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = otlp));

        // Logs correlacionados (trace_id/span_id) — este host não usa Serilog, então o
        // provider OTel entra direto no pipeline do ILogger.
        builder.Logging.AddOpenTelemetry(logging =>
        {
            // #2390: o escopo do ILogger não é editável pelo processador e levaria o RequestPath cru.
            // A correlação (trace_id/span_id) vem da Activity, não do escopo.
            logging.IncludeScopes = false;
            logging.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName));
            logging.AddProcessor(new LogQueRedige());
            logging.AddOtlpExporter(options => options.Endpoint = otlp);
        });
    }
}
