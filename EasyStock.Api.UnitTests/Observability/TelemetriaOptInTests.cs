using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace EasyStock.Api.UnitTests.Observability;

/// <summary>
/// #2107 (F2): a API segue o mesmo contrato opt-in do Web e do Worker (issue 1002). Sem
/// OpenTelemetry:OtlpEndpoint não há provider nem exporter: antes ela caía em localhost:4317 e
/// mandava traces e métricas para o vazio em produção. Com endpoint, os providers existem.
/// </summary>
public class TelemetriaOptInTests
{
    private static ServiceProvider Montar(string? endpoint)
    {
        var services = new ServiceCollection();
        services.AddMetrics();
        services.AddLogging();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenTelemetry:OtlpEndpoint"] = endpoint })
            .Build();
        var env = Substitute.For<IWebHostEnvironment>();
        env.EnvironmentName.Returns("Production");

        services.AddEasyStockObservability(config, env);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sem_endpoint_nao_registra_provider_nem_exporter(string? endpoint)
    {
        using var sp = Montar(endpoint);

        sp.GetService<TracerProvider>().Should().BeNull("sem endpoint a telemetria é no-op, nada de exporter falando com o vazio");
        sp.GetService<MeterProvider>().Should().BeNull();
        sp.GetService<IOperationalMetrics>().Should().NotBeNull("as métricas operacionais continuam injetáveis sem coletor");
    }

    [Fact]
    public void Com_endpoint_registra_tracer_e_meter_provider()
    {
        using var sp = Montar("http://ez-otel:4317");

        sp.GetService<TracerProvider>().Should().NotBeNull();
        sp.GetService<MeterProvider>().Should().NotBeNull();
    }
}
