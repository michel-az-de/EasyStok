using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Serilog.Core;
using Serilog.Events;

namespace EasyStock.Api.Observability;

/// <summary>
/// Singleton que controla o nível mínimo de log do Serilog em tempo real
/// e persiste o estado em ConfiguracaoSistema para sobreviver a restarts.
/// </summary>
public sealed class DiagnosticoModeService(
    IServiceScopeFactory scopeFactory,
    ILogger<DiagnosticoModeService> log)
{
    private const string ConfigKey = "diagnostico:verbose-logging";

    /// <summary>Controla o nível mínimo do Serilog em tempo real.</summary>
    public readonly LoggingLevelSwitch LevelSwitch = new(LogEventLevel.Information);

    public bool IsVerboseEnabled { get; private set; }
    public DateTime? ChangedAt { get; private set; }
    public string? ChangedBy { get; private set; }

    /// <summary>
    /// Chamado no startup após migrations para restaurar o último estado salvo.
    /// </summary>
    public async Task RestoreFromDbAsync()
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var cfg = await db.ConfiguracoesSistema.FirstOrDefaultAsync(x => x.Chave == ConfigKey);
            if (cfg is null) return;

            var verbose = string.Equals(cfg.Valor, "true", StringComparison.OrdinalIgnoreCase);
            ApplyLevel(verbose);
            log.LogInformation("[DiagnosticoMode] Modo restaurado do DB: verbose={Verbose}", verbose);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[DiagnosticoMode] Falha ao restaurar estado — usando padrão (Information).");
        }
    }

    /// <summary>
    /// Altera o modo de logging e persiste em ConfiguracaoSistema.
    /// </summary>
    public async Task SetVerboseAsync(bool enabled, string changedBy)
    {
        ApplyLevel(enabled);
        ChangedAt = DateTime.UtcNow;
        ChangedBy = changedBy;
        log.LogInformation("[DiagnosticoMode] Alterado para verbose={Verbose} por {By}", enabled, changedBy);

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var cfg = await db.ConfiguracoesSistema.FirstOrDefaultAsync(x => x.Chave == ConfigKey);
            if (cfg is null)
            {
                cfg = ConfiguracaoSistema.Criar(
                    ConfigKey,
                    enabled ? "true" : "false",
                    "Logging verbose para coleta de bugs em demos/testes.");
                db.ConfiguracoesSistema.Add(cfg);
            }
            else
            {
                cfg.Atualizar(enabled ? "true" : "false", changedBy);
            }
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[DiagnosticoMode] Falha ao persistir estado no DB.");
        }
    }

    private void ApplyLevel(bool verbose)
    {
        IsVerboseEnabled = verbose;
        LevelSwitch.MinimumLevel = verbose ? LogEventLevel.Debug : LogEventLevel.Information;
    }
}
