using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Reporting;

/// <summary>
/// Implementação do <see cref="IReportRunRepository"/> usando EF Core + Postgres.
/// Queries críticas usam SQL raw para garantir planos eficientes.
/// </summary>
public sealed class ReportRunRepository(EasyStockDbContext db) : IReportRunRepository
{
    private static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(5);

    // ── Leitura ───────────────────────────────────────────────────────────────

    public Task<ReportRun?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ReportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<ReportRun?> FindRecentByParamsHashAsync(
        Guid? empresaId,
        Guid usuarioId,
        string paramsHash,
        TimeSpan window,
        CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow - window;
        return db.ReportRuns
            .AsNoTracking()
            .Where(r =>
                r.EmpresaId == empresaId &&
                r.UsuarioSolicitanteId == usuarioId &&
                r.ParamsHash == paramsHash &&
                r.EnqueuedAt >= cutoff &&
                r.Status != ReportStatus.Canceled &&
                r.Status != ReportStatus.Failed)
            .OrderByDescending(r => r.EnqueuedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ReportRun?> FindByIdempotencyKeyAsync(
        Guid? empresaId,
        Guid usuarioId,
        string idempotencyKey,
        CancellationToken ct) =>
        db.ReportRuns
            .AsNoTracking()
            .Where(r =>
                r.EmpresaId == empresaId &&
                r.UsuarioSolicitanteId == usuarioId &&
                r.IdempotencyKey == idempotencyKey)
            .OrderByDescending(r => r.EnqueuedAt)
            .FirstOrDefaultAsync(ct);

    public Task<IReadOnlyList<ReportRun>> ListMineAsync(
        Guid? empresaId,
        Guid usuarioId,
        ReportListFilter filter,
        int skip,
        int take,
        CancellationToken ct)
    {
        IQueryable<ReportRun> q = db.ReportRuns
            .AsNoTracking()
            .Where(r => r.EmpresaId == empresaId && r.UsuarioSolicitanteId == usuarioId);

        if (filter.Categoria.HasValue)
            q = q.Where(r => r.Categoria == filter.Categoria.Value);
        if (filter.Status.HasValue)
            q = q.Where(r => r.Status == filter.Status.Value);
        if (filter.De.HasValue)
            q = q.Where(r => r.EnqueuedAt >= filter.De.Value);
        if (filter.Ate.HasValue)
            q = q.Where(r => r.EnqueuedAt <= filter.Ate.Value);

        return q
            .OrderByDescending(r => r.EnqueuedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyList<ReportRun>)t.Result, ct);
    }

    public Task<int> CountRunningForOwnerAsync(
        Guid? empresaId,
        Guid? adminUsuarioId,
        CancellationToken ct)
    {
        if (empresaId.HasValue)
            return db.ReportRuns
                .AsNoTracking()
                .CountAsync(r => r.EmpresaId == empresaId && r.Status == ReportStatus.Running, ct);

        return db.ReportRuns
            .AsNoTracking()
            .CountAsync(r => r.UsuarioSolicitanteId == adminUsuarioId && r.Status == ReportStatus.Running, ct);
    }

    // ── Escrita ───────────────────────────────────────────────────────────────

    public async Task AddAsync(ReportRun run, CancellationToken ct)
    {
        await db.ReportRuns.AddAsync(run, ct);
        await db.SaveChangesAsync(ct);
    }

    // ── Worker: picking e locking ─────────────────────────────────────────────

    /// <summary>
    /// Seleciona até <paramref name="batchSize"/> runs pendentes respeitando
    /// round-robin por tenant (DISTINCT ON empresa_id) — ADR-R04 + B-02.
    /// Admin runs (empresa_id NULL) usam usuário como partição.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> PickNextBatchAsync(int batchSize, CancellationToken ct)
    {
        // B-02: DISTINCT ON é mais eficiente que ROW_NUMBER() com LIMIT externo.
        // Índice ix_report_runs_pending_picker_tenant cobre o WHERE status=0 AND contexto=1.
        // Para Admin (contexto=2), índice ix_report_runs_pending_picker_admin cobre.
        const string sql = """
            -- Tenant runs (round-robin por empresa_id)
            SELECT DISTINCT ON (empresa_id) id
              FROM public.report_runs
             WHERE status = 0
               AND contexto = 1
               AND (next_attempt_at IS NULL OR next_attempt_at <= now())
             ORDER BY empresa_id, COALESCE(next_attempt_at, enqueued_at), enqueued_at
             LIMIT {0}

            UNION ALL

            -- Admin SaaS runs (round-robin por usuário)
            SELECT DISTINCT ON (usuario_solicitante_id) id
              FROM public.report_runs
             WHERE status = 0
               AND contexto = 2
               AND (next_attempt_at IS NULL OR next_attempt_at <= now())
             ORDER BY usuario_solicitante_id, COALESCE(next_attempt_at, enqueued_at), enqueued_at
             LIMIT {0}
            """;

        // Usar FromSqlRaw com FormattableString — Npgsql não suporta {0} direto em LIMIT;
        // precisamos de string interpolada segura. batchSize é int — sem risco de injection.
        var formattedSql = string.Format(sql, batchSize);

        var ids = await db.ReportRuns
            .FromSqlRaw(formattedSql)
            .AsNoTracking()
            .Select(r => r.Id)
            .Take(batchSize)
            .ToListAsync(ct);

        return ids;
    }

    /// <summary>
    /// Tenta adquirir advisory lock transacional para a run (B-01 fix).
    /// A chave é derivada via hashtextextended — determinístico, 64 bits.
    /// </summary>
    public async Task<bool> TryAcquireLockAsync(Guid runId, CancellationToken ct)
    {
        // B-01: uuid não cabe em bigint. Usar hashtextextended que retorna bigint.
        var result = await db.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock(hashtextextended({0}::text, 0)) AS \"Value\"",
                runId.ToString())
            .FirstOrDefaultAsync(ct);

        return result;
    }

    /// <summary>
    /// Heartbeat: estende o lease somente se ainda sou o dono (B-03 fix).
    /// </summary>
    public async Task HeartbeatAsync(Guid runId, TimeSpan leaseDuration, CancellationToken ct)
    {
        var newExpiry = DateTimeOffset.UtcNow + leaseDuration;
        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE public.report_runs
               SET lease_expires_at = {0},
                   alterado_em      = now()
             WHERE id     = {1}
               AND status = 1
               AND lease_expires_at > now()
            """,
            [newExpiry, runId],
            ct);
    }

    // ── Watchdog ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Recicla runs com lease expirado. Incrementa tentativas; se esgotadas → Failed terminal.
    /// Executa dentro de advisory lock global para watchdog (G-15).
    /// </summary>
    public async Task<int> ReclaimExpiredLeasesAsync(CancellationToken ct)
    {
        // G-15: apenas 1 worker executa o watchdog por vez.
        var gotLock = await db.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock(hashtextextended('reporting:watchdog', 0)) AS \"Value\"")
            .FirstOrDefaultAsync(ct);

        if (!gotLock) return 0;

        // Runs que podem ser recicladas (tentativas < max)
        var pendingReclaim = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE public.report_runs
               SET status          = 0,
                   lease_expires_at = NULL,
                   next_attempt_at  = now()
                                      + (interval '1 second' * 30 * pow(2, tentativas - 1))
                                      + (random() * interval '12 seconds' - interval '6 seconds'),
                   alterado_em      = now()
             WHERE status           = 1
               AND lease_expires_at < now()
               AND tentativas       < max_tentativas
            """,
            ct);

        // Runs terminais (tentativas >= max)
        var terminalReclaim = await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE public.report_runs
               SET status          = 4,
                   finished_at      = now(),
                   lease_expires_at = NULL,
                   error_class      = 'LeaseLossTerminal',
                   error_message    = 'Excedeu o número máximo de tentativas após o Worker não confirmar a execução.',
                   alterado_em      = now()
             WHERE status           = 1
               AND lease_expires_at < now()
               AND tentativas       >= max_tentativas
            """,
            ct);

        return pendingReclaim + terminalReclaim;
    }

    // ── GC de artefatos ───────────────────────────────────────────────────────

    public async Task<int> PurgeExpiredArtifactsAsync(int batchSize, CancellationToken ct)
    {
        // Carrega as runs elegíveis para GC
        var expiredRuns = await db.ReportRuns
            .Where(r =>
                r.Status == ReportStatus.Succeeded &&
                r.ArtifactStorageKey != null &&
                r.ExpiresAt < DateTimeOffset.UtcNow)
            .Take(batchSize)
            .ToListAsync(ct);

        if (expiredRuns.Count == 0) return 0;

        foreach (var run in expiredRuns)
            run.MarkArtifactPurged();

        await db.SaveChangesAsync(ct);
        return expiredRuns.Count;
    }

    public async Task<int> PurgeAllForTenantAsync(Guid empresaId, CancellationToken ct)
    {
        var runs = await db.ReportRuns
            .Where(r => r.EmpresaId == empresaId && r.ArtifactStorageKey != null)
            .ToListAsync(ct);

        if (runs.Count == 0) return 0;

        foreach (var run in runs)
            run.MarkArtifactPurged();

        await db.SaveChangesAsync(ct);
        return runs.Count;
    }
}
