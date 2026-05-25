namespace EasyStock.Api.Observability;

/// <summary>
/// Captura o estado da infraestrutura resolvido durante o startup.
/// Registrado como singleton para ser acessível por health checks e diagnósticos.
/// </summary>
public sealed class ResolvedInfrastructureState
{
    public string DatabaseProvider { get; init; } = "unknown";
    public string ConfiguredProvider { get; init; } = "unknown";
    public bool IsFallback { get; init; }
    public DateTimeOffset StartupTime { get; init; } = DateTimeOffset.UtcNow;
    public string Environment { get; init; } = "unknown";
    public bool? MigrationsApplied { get; set; }
    public string? MigrationError { get; set; }
}
