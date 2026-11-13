using EasyStock.Api.Observability.HealthChecks;
using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Api.UnitTests.Observability.HealthChecks;

/// <summary>
/// #2376: o disco da VPS é dividido pelo Postgres e pelo OpenObserve, que só tem limite por tempo (30 dias). Um pico de
/// erros pode enchê-lo. O /health/disco responde 503 abaixo do mínimo livre, e o vigia externo (F1) abre a issue.
/// </summary>
public class DiscoHealthCheckTests
{
    private const long Giga = 1024L * 1024 * 1024;

    private static Task<HealthCheckResult> Rodar(long livre, long total, double minimoPercentual = 15) =>
        new DiscoHealthCheck(() => (livre, total), minimoPercentual).CheckHealthAsync(new HealthCheckContext());

    [Fact]
    public async Task Saudavel_ComFolga()
    {
        var resultado = await Rodar(livre: 40 * Giga, total: 100 * Giga);

        resultado.Status.Should().Be(HealthStatus.Healthy);
        resultado.Data["livrePercentual"].Should().Be(40.0);
    }

    [Fact]
    public async Task NaoSaudavel_AbaixoDoMinimo()
    {
        var resultado = await Rodar(livre: 10 * Giga, total: 100 * Giga);

        resultado.Status.Should().Be(HealthStatus.Unhealthy);
        resultado.Description.Should().Contain("10").And.Contain("15");
    }

    [Fact]
    public async Task NaoSaudavel_QuandoNaoConsegueMedir()
    {
        var resultado = await new DiscoHealthCheck(() => throw new IOException("sem acesso"), 15)
            .CheckHealthAsync(new HealthCheckContext());

        resultado.Status.Should().Be(HealthStatus.Unhealthy);
    }
}
