using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Infra.Async.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EasyStock.Worker.BackgroundServices;

/// <summary>
/// Watchdog do motor de relatórios.
/// Responsabilidades:
///   1. Reclamar leases expirados — runs presas em Running voltam para Pending
///      (ou marcadas Failed definitivo se tentativas esgotadas). (ADR-R03, §6.3)
///   2. Garbage-collect de artefatos expirados — NULLifica artifact_storage_key
///      e emite deleção física do arquivo no storage. (§B-15)
/// Advisory lock global (§G-15): <see cref="IReportRunRepository.ReclaimExpiredLeasesAsync"/>
/// já adquire o lock internamente; este serviço não o adquire de novo.
/// </summary>
public sealed class ReportWatchdogBackgroundService(
    IServiceProvider serviceProvider,
    ReportingMetricsService metrics,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    ILogger<ReportWatchdogBackgroundService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var enabled = configuration.GetValue("Reporting:Runner:Enabled", defaultValue: true);
        if (!enabled)
        {
            logger.LogInformation("ReportWatchdogBackgroundService desabilitado via Reporting:Runner:Enabled=false.");
            return;
        }

        var leaseReclaimIntervalSec = configuration.GetValue("Reporting:Watchdog:LeaseReclaimIntervalSeconds", 60);
        var artifactGcIntervalHours = configuration.GetValue("Reporting:Watchdog:ArtifactGcIntervalHours", 6.0);
        var artifactGcBatchSize = configuration.GetValue("Reporting:Watchdog:ArtifactGcBatchSize", 100);

        var leaseReclaimInterval = TimeSpan.FromSeconds(leaseReclaimIntervalSec);
        var artifactGcInterval = TimeSpan.FromHours(artifactGcIntervalHours);

        logger.LogInformation(
            "ReportWatchdogBackgroundService iniciado — leaseReclaim={LeaseInterval}s artifactGc={GcInterval}h.",
            leaseReclaimIntervalSec, artifactGcIntervalHours);

        // Aguarda o app subir antes do primeiro ciclo.
        try { await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken); }
        catch (OperationCanceledException) { return; }

        var lastArtifactGc = DateTimeOffset.MinValue;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunLeaseReclaimAsync(stoppingToken);

                // GC de artefatos é mais esparso — roda a cada N horas.
                if (DateTimeOffset.UtcNow - lastArtifactGc >= artifactGcInterval)
                {
                    await RunArtifactGcAsync(artifactGcBatchSize, stoppingToken);
                    lastArtifactGc = DateTimeOffset.UtcNow;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "ReportWatchdogBackgroundService: erro no ciclo de watchdog.");
            }

            try { await Task.Delay(leaseReclaimInterval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("ReportWatchdogBackgroundService finalizado.");
    }

    // ── Lease reclaim ──────────────────────────────────────────────────────────

    private async Task RunLeaseReclaimAsync(CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReportRunRepository>();

        // ReclaimExpiredLeasesAsync adquire advisory lock de watchdog internamente (G-15).
        int reclaimed = await repo.ReclaimExpiredLeasesAsync(ct);

        if (reclaimed > 0)
        {
            logger.LogInformation(
                "ReportWatchdog: {Count} lease(s) expirado(s) reclamado(s).", reclaimed);
            metrics.RecordLeasesReclaimed("tenant", reclaimed);
        }
    }

    // ── Artifact GC ───────────────────────────────────────────────────────────

    private async Task RunArtifactGcAsync(int batchSize, CancellationToken ct)
    {
        using var scope = serviceProvider.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IReportRunRepository>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var db = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();

        // Advisory lock: apenas 1 Worker executa o GC de artefatos por vez.
        bool gotLock = await TryAcquireWatchdogLockAsync(db, ct);
        if (!gotLock) return;

        // Carrega as storage keys antes de NULLificar no DB.
        var storageKeys = await db.ReportRuns
            .Where(r =>
                r.Status == Domain.Reporting.ReportStatus.Succeeded &&
                r.ArtifactStorageKey != null &&
                r.ExpiresAt < DateTimeOffset.UtcNow)
            .Take(batchSize)
            .Select(r => r.ArtifactStorageKey!)
            .ToListAsync(ct);

        if (storageKeys.Count == 0) return;

        // Marca como purgados no DB.
        int purged = await repo.PurgeExpiredArtifactsAsync(batchSize, ct);

        // Deleção física no storage (best-effort).
        int deleted = 0;
        foreach (var key in storageKeys)
        {
            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "ReportWatchdog: falha ao deletar artefato '{Key}' do storage (best-effort).", key);
            }
        }

        if (purged > 0)
        {
            logger.LogInformation(
                "ReportWatchdog: {DbPurged} artefato(s) expirado(s) purgado(s) no DB; {StorageDeleted} deletado(s) do storage.",
                purged, deleted);
            metrics.RecordArtifactsPurged("tenant", "Expired", purged);
        }
    }

    // ── Advisory lock global do watchdog (§G-15) ──────────────────────────────

    private static async Task<bool> TryAcquireWatchdogLockAsync(
        EasyStockDbContext db, CancellationToken ct)
    {
        // hashtextextended('reporting:watchdog', 0) → bigint — determinístico.
        // Esta transação não é explícita aqui; o lock fica no nível de sessão até o scope fechar.
        return await db.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_lock(hashtextextended('reporting:watchdog-gc', 0)) AS \"Value\"")
            .FirstAsync(ct);
    }
}
