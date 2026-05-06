using System.Collections.Concurrent;
using System.Text.Json;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services;

/// <summary>
/// Registro de uma etapa individual do seed (level, mensagem, % concluído).
/// </summary>
public sealed record SeedEtapa(string Ts, string Level, string Mensagem, int Percent);

/// <summary>
/// Estado em memória de um run de seed. Atualizado pelo SeedRunner em background
/// e lido pelos endpoints de polling da UI.
/// </summary>
public sealed class SeedRunState
{
    public Guid RunId { get; init; }
    public string AdminEmail { get; init; } = "";
    public string TipoSeed { get; init; } = "";
    public string? Volume { get; init; }
    public DateTime StartedAt { get; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Running | Success | Failed | RolledBack</summary>
    public string Status { get; set; } = "Running";
    public int Percent { get; set; }
    public string CurrentStep { get; set; } = "Iniciando…";
    public string? Erro { get; set; }
    public string? Resumo { get; set; }

    private readonly List<SeedEtapa> _etapas = [];
    public IReadOnlyList<SeedEtapa> Etapas => _etapas;

    internal void AddEtapa(SeedEtapa e) => _etapas.Add(e);
}

/// <summary>
/// Singleton. Guarda em memória o estado de cada run de seed em andamento.
/// Ao concluir, persiste o log final em <see cref="SeedRunLog"/> no DB.
/// Cap de 50 runs (FIFO — o mais antigo é descartado pra evitar leak).
/// </summary>
public sealed class SeedProgressService(IServiceProvider services, ILogger<SeedProgressService> log)
{
    private static readonly ConcurrentDictionary<Guid, SeedRunState> _runs = new();
    private static readonly Queue<Guid> _order = new();
    private const int MaxRuns = 50;

    public SeedRunState Create(Guid runId, string adminEmail, string tipo, string? volume)
    {
        var state = new SeedRunState { RunId = runId, AdminEmail = adminEmail, TipoSeed = tipo, Volume = volume };
        _runs[runId] = state;

        lock (_order)
        {
            _order.Enqueue(runId);
            while (_order.Count > MaxRuns)
                _runs.TryRemove(_order.Dequeue(), out _);
        }

        return state;
    }

    public SeedRunState? Get(Guid runId) => _runs.TryGetValue(runId, out var s) ? s : null;

    public void Report(Guid runId, int percent, string mensagem, string level = "info")
    {
        if (!_runs.TryGetValue(runId, out var s)) return;
        s.Percent = percent;
        s.CurrentStep = mensagem;
        s.AddEtapa(new SeedEtapa(
            DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            level,
            mensagem,
            percent));
        log.LogInformation("[Seed:{RunId}] {Percent}% — {Msg}", runId.ToString()[..8], percent, mensagem);
    }

    public void Success(Guid runId, string resumo)
    {
        if (!_runs.TryGetValue(runId, out var s)) return;
        s.Status = "Success";
        s.Percent = 100;
        s.CurrentStep = "Concluído com sucesso.";
        s.Resumo = resumo;
        s.CompletedAt = DateTime.UtcNow;
        s.AddEtapa(new SeedEtapa(DateTime.UtcNow.ToString("HH:mm:ss.fff"), "success", resumo, 100));
        _ = PersistAsync(s);
    }

    public void Failure(Guid runId, string erro, bool rolledBack = false)
    {
        if (!_runs.TryGetValue(runId, out var s)) return;
        s.Status = rolledBack ? "RolledBack" : "Failed";
        s.Erro = erro;
        s.CurrentStep = rolledBack ? "Rollback efetuado — dados anteriores preservados." : "Falhou.";
        s.CompletedAt = DateTime.UtcNow;
        s.AddEtapa(new SeedEtapa(DateTime.UtcNow.ToString("HH:mm:ss.fff"), "error",
            rolledBack ? $"Rollback — {erro}" : $"Erro — {erro}", s.Percent));
        _ = PersistAsync(s);
    }

    private async Task PersistAsync(SeedRunState s)
    {
        try
        {
            await using var scope = services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

            var existing = await db.SeedRunLogs.FirstOrDefaultAsync(x => x.Id == s.RunId);
            if (existing is null)
            {
                existing = new SeedRunLog { Id = s.RunId };
                db.SeedRunLogs.Add(existing);
            }

            existing.AdminEmail = s.AdminEmail;
            existing.TipoSeed = s.TipoSeed;
            existing.Volume = s.Volume;
            existing.StartedAt = s.StartedAt;
            existing.CompletedAt = s.CompletedAt;
            existing.Status = s.Status;
            existing.Erro = s.Erro;
            existing.Resumo = s.Resumo;
            existing.EtapasJson = JsonSerializer.Serialize(s.Etapas);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "[Seed:{RunId}] Falha ao persistir SeedRunLog — progresso perdido no restart.", s.RunId);
        }
    }
}
