using EasyStock.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace EasyStock.Web.UnitTests.Infrastructure;

/// <summary>
/// Trava o contrato opt-in da telemetria (issue 1002): sem OpenTelemetry:OtlpEndpoint
/// configurado, NENHUM provider OTel entra no container (dev local e CI não podem ganhar
/// exporter tentando falar com collector inexistente); com endpoint, o TracerProvider
/// precisa existir — é ele que sustenta o trace distribuído Web -> Api -> Postgres.
/// </summary>
public class TelemetrySetupTests
{
    private static WebApplicationBuilder CriarBuilder(string? endpoint)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OpenTelemetry:OtlpEndpoint"] = endpoint,
        });
        return builder;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_endpoint_configurado_nao_registra_tracer_provider(string? endpoint)
    {
        var builder = CriarBuilder(endpoint);

        builder.AddEasyStockTelemetry("EasyStock.Web");

        using var app = builder.Build();
        app.Services.GetService<TracerProvider>().Should().BeNull(
            "sem endpoint a telemetria é no-op — nada de exporter em dev/CI");
    }

    [Fact]
    public void Com_endpoint_configurado_registra_tracer_provider()
    {
        var builder = CriarBuilder("http://otel-collector:4317");

        builder.AddEasyStockTelemetry("EasyStock.Web");

        using var app = builder.Build();
        app.Services.GetService<TracerProvider>().Should().NotBeNull();
    }
}
