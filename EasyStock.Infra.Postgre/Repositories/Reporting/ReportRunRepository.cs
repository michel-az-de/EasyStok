using EasyStock.Application.Ports.Output.Reporting;
using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Security;

namespace EasyStock.Infra.Postgre.Repositories.Reporting;

/// <summary>
/// Implementação do <see cref="IReportRunRepository"/> usando EF Core + Postgres.
/// Queries críticas usam SQL raw para garantir planos eficientes.
/// </summary>
public sealed class ReportRunRepository(EasyStockDbContext db) : IReportRunRepository
{
    // ── Leitura ───────────────────────────────────────────────────────────────

    public Task<ReportRun?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.ReportRuns
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public Task<ReportRun?> FindRecentByParamsHashAsync(
        Guid?             empresaId,
        Guid              usuarioId,
        string            paramsHash,
        TimeSpan          window,
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
        Guid?             empresaId,
        Guid              usuarioId,
        string            idempotencyKey,
        CancellationToken ct) =>
        db.ReportRuns
            .AsNoTracking()
            .Where(r =>
                r.EmpresaId == empresaId &&
                r.UsuarioSolicitanteId == usuarioId &&
                r.IdempotencyKey == idempotencyKey)
            .OrderByDescending(r => r.EnqueuedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<ReportRun>> ListMineAsync(
        Guid?             empresaId,
        Guid              usuarioId,
        ReportListFilter  filter,
        int               skip,
        int               take,
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

        return await q
            .OrderByDescending(r => r.EnqueuedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
    }

    // ── Escrita ───────────────────────────────────────────────────────────────

    public async Task AddAsync(ReportRun run, CancellationToken ct)
    {
        await db.ReportRuns.AddAsync(run, ct);
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> UpdateAsync(ReportRun run, CancellationToken ct)
    {
        db.ReportRuns.Update(run);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            db.Entry(run).State = EntityState.Detached;
            return false;
        }
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
        //
        // Duas armadilhas resolvidas aqui (a query nunca funcionou — latente porque o
        // DbContext do Worker não construía até #877 e as IntegrationTests de Postgre
        // estão fora do CI):
        //
        //  (1) Cada braço do UNION ALL tem ORDER BY + LIMIT próprios → PRECISA de
        //      parênteses, senão é "syntax error at or near UNION" (42601).
        //  (2) Composição LINQ sobre FromSqlRaw de um UNION é "non-composable": no EF
        //      estrito (SDK 10.0.301 do CI, não no 10.0.201 local) `.FromSqlRaw(union)
        //      .Select(r => r.Id).Take(n)` lança "'FromSql' was called with
        //      non-composable SQL and with a query composing over it". Por isso usamos
        //      db.Database.SqlQueryRaw<Guid> (query escalar terminal, SEM composição) —
        //      o LIMIT externo vai DENTRO do SQL e a coluna é aliased "Value" (convenção
        //      do EF para SqlQuery escalar).
        //
        // #3108: report_runs tem a policy tenant_isolation. O picker atravessa empresas por
        // natureza (o Worker não tem tenant), então lê sob bypass; só devolve ids.
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);
        const string sql = """
            SELECT id AS "Value"
              FROM (
                -- Tenant runs (round-robin por empresa_id)
                (SELECT DISTINCT ON (empresa_id) id
                   FROM public.report_runs
                  WHERE status = 0
                    AND contexto = 1
                    AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                  ORDER BY empresa_id, COALESCE(next_attempt_at, enqueued_at), enqueued_at
                  LIMIT {0})

                UNION ALL

                -- Admin SaaS runs (round-robin por usuário)
                (SELECT DISTINCT ON (usuario_solicitante_id) id
                   FROM public.report_runs
                  WHERE status = 0
                    AND contexto = 2
                    AND (next_attempt_at IS NULL OR next_attempt_at <= now())
                  ORDER BY usuario_solicitante_id, COALESCE(next_attempt_at, enqueued_at), enqueued_at
                  LIMIT {0})
              ) sub
             LIMIT {0}
            """;

        // string.Format (não parâmetro): Npgsql não aceita {0} parametrizado em LIMIT.
        // batchSize é int — sem risco de injection.
        var formattedSql = string.Format(sql, batchSize);

        var ids = await db.Database
            .SqlQueryRaw<Guid>(formattedSql)
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

    public async Task<ReportRun?> TryStartAsync(Guid runId, TimeSpan leaseDuration, CancellationToken ct)
    {
        // #3108: a reserva acontece antes de existir o tenant da execução (IReportExecutionScope.Begin vem
        // depois, com a empresa da run). Bypass ligado antes de abrir a transação e só durante ela.
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);

        // Sem retry: o DbContext do Worker usa EnableRetryOnFailure, que recusa transação aberta à mão
        // (BeginTransactionAsync direto lançava InvalidOperationException na primeira consulta).
        return await db.ExecuteInTransactionSemRetryAsync<ReportRun?>(async token =>
        {
            if (!await TryAcquireLockAsync(runId, token)) return null;

            var run = await GetByIdAsync(runId, token);
            if (run is null || !run.TryStart(leaseDuration)) return null;

            // GetByIdAsync le sem tracking: sem marcar Modified o TryStart nao era gravado,
            // a run seguia Pending, voltava no proximo polling e rodava em duplicata (#1507).
            db.Entry(run).State = EntityState.Modified;
            await db.SaveChangesAsync(token);
            return run;
        }, ct);
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
    /// Run em Canceling com lease vencido (worker morto) é fechada como Canceled (#2773).
    /// Devolve as runs fechadas sem sucesso, para o watchdog apagar o envio interrompido delas (#2788).
    /// Executa dentro de advisory lock global para watchdog (G-15).
    /// </summary>
    public async Task<ReportRunReciclagem> ReclaimExpiredLeasesAsync(CancellationToken ct)
    {
        // #3108: o watchdog recicla runs de todas as empresas; sem bypass os UPDATEs afetam zero linhas, sem erro.
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);

        // G-15: apenas 1 worker executa o watchdog por vez.
        var gotLock = await db.Database
            .SqlQueryRaw<bool>(
                "SELECT pg_try_advisory_xact_lock(hashtextextended('reporting:watchdog', 0)) AS \"Value\"")
            .FirstOrDefaultAsync(ct);

        if (!gotLock) return new ReportRunReciclagem(0, []);

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

        // Runs terminais (tentativas >= max). RETURNING (#2788): quem fechou sai da mesma instrução que fechou.
        // SqlQueryRaw não compõe UPDATE ... RETURNING (embrulharia em subselect): lista sem compor.
        var terminalReclaim = await db.Database.SqlQueryRaw<Guid>(
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
            RETURNING id AS "Value"
            """)
            .ToListAsync(ct);

        // #2773: Canceling com lease vencido = worker morreu antes de fechar o cancelamento.
        // Espelha ReportRun.MarkCanceled (Canceled, finished_at, lease nulo). Worker vivo fecha
        // sozinho dentro do prazo; o lease não é renovado em Canceling (heartbeat só em Running).
        var canceledReclaim = await db.Database.SqlQueryRaw<Guid>(
            """
            UPDATE public.report_runs
               SET status          = 3,
                   finished_at      = now(),
                   lease_expires_at = NULL,
                   alterado_em      = now()
             WHERE status           = 5
               AND lease_expires_at < now()
            RETURNING id AS "Value"
            """)
            .ToListAsync(ct);

        List<Guid> fechadas = [.. terminalReclaim, .. canceledReclaim];
        IReadOnlyList<ReportRun> semSucesso = fechadas.Count == 0
            ? []
            : await db.ReportRuns.AsNoTracking().Where(r => fechadas.Contains(r.Id)).ToListAsync(ct);

        return new ReportRunReciclagem(pendingReclaim + fechadas.Count, semSucesso);
    }

    // ── GC de artefatos ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<string>> PurgeExpiredArtifactsAsync(int batchSize, CancellationToken ct)
    {
        // Seleciona as runs elegíveis UMA vez, com ordem determinística (#929). Antes o
        // watchdog fazia uma SEGUNDA query Take(batchSize) sem OrderBy para obter as storage
        // keys, e o Postgres não garante o mesmo conjunto entre dois Take(N) quando há mais
        // elegíveis que batchSize — arquivos deletados divergiam das linhas zeradas no DB.
        // #3108: GC de todas as empresas, sob bypass (cobre a leitura e o SaveChanges).
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);
        var expiredRuns = await db.ReportRuns
            .Where(r =>
                r.Status == ReportStatus.Succeeded &&
                r.ArtifactStorageKey != null &&
                r.ExpiresAt < DateTimeOffset.UtcNow)
            .OrderBy(r => r.ExpiresAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (expiredRuns.Count == 0) return Array.Empty<string>();

        // Captura as chaves ANTES de MarkArtifactPurged (que zera ArtifactStorageKey).
        var storageKeys = expiredRuns.Select(r => r.ArtifactStorageKey!).ToList();

        foreach (var run in expiredRuns)
            run.MarkArtifactPurged();

        await db.SaveChangesAsync(ct);
        return storageKeys;
    }

    // ── Worker: fechamento atômico (#2764) ───────────────────────────────────

    public async Task<ReportRunDesfecho> FecharAsync(
        ReportRun run, Action<ReportRun> desfechoNormal, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Trava a linha desta tentativa. SqlQueryRaw não compõe com FOR UPDATE: lista e pega o primeiro em memória.
        var status = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT status AS "Value"
                  FROM public.report_runs
                 WHERE id         = {0}
                   AND tentativas = {1}
                   FOR UPDATE
                """,
                run.Id, run.Tentativas)
            .ToListAsync(ct);

        if (status.Count == 0 || (status[0] != 1 && status[0] != 5))
            return ReportRunDesfecho.TentativaPerdida;

        // Cancelamento pedido durante a geração: vence qualquer desfecho normal (sucesso, retry ou falha).
        var desfecho = status[0] == 5 ? ReportRunDesfecho.CanceladoPeloUsuario : ReportRunDesfecho.Gravado;
        if (desfecho == ReportRunDesfecho.CanceladoPeloUsuario)
            run.MarkCanceled();
        else
            desfechoNormal(run);

        // Alinha o xmin DEPOIS da trava: o heartbeat avança o xmin com UPDATE direto.
        var versoes = await db.Database
            .SqlQueryRaw<long>(
                """
                SELECT xmin::text::bigint AS "Value"
                  FROM public.report_runs
                 WHERE id = {0}
                """,
                run.Id)
            .ToListAsync(ct);

        var entry = db.Entry(run);
        entry.State = EntityState.Modified;
        if (versoes.Count == 1)
            entry.Property(r => r.Xmin).OriginalValue = (uint)versoes[0];

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return desfecho;
    }
}
