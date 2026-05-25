using EasyStock.Application.Reporting;
using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Ports.Output.Reporting;

/// <summary>
/// Port de persistência do agregado <see cref="ReportRun"/>.
/// </summary>
public interface IReportRunRepository
{
    Task<ReportRun?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Procura uma run recente com o mesmo owner + paramsHash dentro da janela de idempotência.
    /// </summary>
    Task<ReportRun?> FindRecentByParamsHashAsync(
        Guid? empresaId,
        Guid usuarioId,
        string paramsHash,
        TimeSpan window,
        CancellationToken ct);

    /// <summary>
    /// Procura run com idempotency_key forte (sem limite de tempo).
    /// </summary>
    Task<ReportRun?> FindByIdempotencyKeyAsync(
        Guid? empresaId,
        Guid usuarioId,
        string idempotencyKey,
        CancellationToken ct);

    Task AddAsync(ReportRun run, CancellationToken ct);

    Task<IReadOnlyList<ReportRun>> ListMineAsync(
        Guid? empresaId,
        Guid usuarioId,
        ReportListFilter filter,
        int skip,
        int take,
        CancellationToken ct);

    /// <summary>
    /// Seleciona até <paramref name="batchSize"/> runs pendentes para execução,
    /// respeitando round-robin por tenant (uma run por tenant por batch).
    /// </summary>
    Task<IReadOnlyList<Guid>> PickNextBatchAsync(int batchSize, CancellationToken ct);

    /// <summary>
    /// Tenta adquirir advisory lock por run_id (pg_try_advisory_xact_lock).
    /// Deve ser chamado dentro de uma transação.
    /// </summary>
    Task<bool> TryAcquireLockAsync(Guid runId, CancellationToken ct);

    /// <summary>Atualiza o lease_expires_at sem alterar outros campos.</summary>
    Task HeartbeatAsync(Guid runId, TimeSpan leaseDuration, CancellationToken ct);

    /// <summary>
    /// Watchdog: move runs com lease expirado de volta para Pending (ou Failed se tentativas esgotadas).
    /// Retorna IDs das runs afetadas.
    /// </summary>
    Task<int> ReclaimExpiredLeasesAsync(CancellationToken ct);

    /// <summary>
    /// GC de artefatos: remove artifact_storage_key de runs expiradas.
    /// Retorna quantidade de runs atualizadas.
    /// </summary>
    Task<int> PurgeExpiredArtifactsAsync(int batchSize, CancellationToken ct);

    /// <summary>Retorna quantas runs estão em Running para o owner informado.</summary>
    Task<int> CountRunningForOwnerAsync(Guid? empresaId, Guid? adminUsuarioId, CancellationToken ct);

    /// <summary>LGPD: purga todos os artefatos de um tenant.</summary>
    Task<int> PurgeAllForTenantAsync(Guid empresaId, CancellationToken ct);
}
