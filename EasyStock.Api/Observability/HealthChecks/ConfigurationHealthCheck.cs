using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EasyStock.Api.Observability.HealthChecks;

public sealed class ConfigurationHealthCheck(
    IConfiguration configuration,
    ResolvedInfrastructureState infraState,
    IWebHostEnvironment environment) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var problems = new List<string>();

        var jwtKey = configuration["Jwt:SecretKey"];
        if (string.IsNullOrWhiteSpace(jwtKey))
            problems.Add("Jwt:SecretKey ausente");
        else if (jwtKey.Length < 32)
            problems.Add("Jwt:SecretKey muito curta (< 32 caracteres)");

        if (infraState.DatabaseProvider is "postgresql" &&
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection")))
            problems.Add("ConnectionStrings:DefaultConnection ausente para PostgreSQL");

        if (!environment.IsDevelopment())
        {
            var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
            if (origins is not { Length: > 0 })
                problems.Add("Cors:AllowedOrigins ausente em ambiente nao-Development");
        }

        if (infraState.IsFallback)
            problems.Add($"Banco em fallback: configurado '{infraState.ConfiguredProvider}', usando '{infraState.DatabaseProvider}'");

        if (problems.Count == 0)
            return Task.FromResult(HealthCheckResult.Healthy("Configuracoes criticas verificadas."));

        var description = string.Join("; ", problems);
        return Task.FromResult(
            problems.Any(p => p.Contains("ausente"))
                ? HealthCheckResult.Unhealthy(description)
                : HealthCheckResult.Degraded(description));
    }
}
