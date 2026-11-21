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
        Guid?          empresaId,
        Guid           usuarioId,
        string         paramsHash,
        TimeSpan       window,
        CancellationToken ct);

    /// <summary>
    /// Procura run com idempotency_key forte (sem limite de tempo).
    /// </summary>
    Task<ReportRun?> FindByIdempotencyKeyAsync(
        Guid?  empresaId,
        Guid   usuarioId,
        string idempotencyKey,
        CancellationToken ct);

    Task AddAsync(ReportRun run, CancellationToken ct);

    /// <summary>
    /// Grava a mudança de estado de uma run lida por <see cref="GetByIdAsync"/>. Usa o xmin como
    /// concorrência: devolve false se o Worker (ou outro pedido) alterou a run no meio, e o chamador relê.
    /// </summary>
    Task<bool> UpdateAsync(ReportRun run, CancellationToken ct);

    Task<IReadOnlyList<ReportRun>> ListMineAsync(
        Guid?           empresaId,
        Guid            usuarioId,
        ReportListFilter filter,
        int             skip,
        int             take,
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
    /// Run em Canceling com lease vencido (worker morto) é fechada como Canceled (#2773).
    /// Devolve o total reciclado e as runs que esta chamada fechou sem sucesso (#2788).
    /// </summary>
    Task<ReportRunReciclagem> ReclaimExpiredLeasesAsync(CancellationToken ct);

    /// <summary>
    /// GC de artefatos: remove artifact_storage_key de runs expiradas (uma única seleção,
    /// ordem determinística). Retorna as storage keys das runs purgadas, para o caller
    /// deletar exatamente esses arquivos do storage (#929 — evita divergência DB vs storage).
    /// </summary>
    Task<IReadOnlyList<string>> PurgeExpiredArtifactsAsync(int batchSize, CancellationToken ct);

    /// <summary>
    /// Fecha a execução do Worker de forma atômica (#2764): trava a linha, lê o status gravado e decide o desfecho.
    /// Se a pessoa pediu o cancelamento durante a geração (Canceling), grava Canceled e ignora o desfecho normal
    /// (sucesso ou falha), para o relatório cancelado nunca ser entregue nem reenfileirado. Se a tentativa foi
    /// perdida (a linha não está mais Running/Canceling com o mesmo número de tentativas), não grava nada.
    /// </summary>
    Task<ReportRunDesfecho> FecharAsync(ReportRun run, Action<ReportRun> desfechoNormal, CancellationToken ct);
}

/// <summary>Resultado de <see cref="IReportRunRepository.ReclaimExpiredLeasesAsync"/>.</summary>
/// <param name="Total">Runs recicladas: devolvidas à fila, falhadas por tentativas esgotadas ou canceladas.</param>
/// <param name="FechadasSemSucesso">
/// Runs que esta chamada fechou como Failed ou Canceled. Nenhuma tentativa vai concluir o envio delas (#2788).
/// </param>
public sealed record ReportRunReciclagem(int Total, IReadOnlyList<ReportRun> FechadasSemSucesso);

/// <summary>Resultado de <see cref="IReportRunRepository.FecharAsync"/>.</summary>
public enum ReportRunDesfecho
{
    /// <summary>O desfecho normal (sucesso ou falha) foi gravado.</summary>
    Gravado,

    /// <summary>A pessoa cancelou durante a geração: a run foi gravada como Canceled.</summary>
    CanceladoPeloUsuario,

    /// <summary>A linha não pertence mais a esta tentativa: nada foi gravado.</summary>
    TentativaPerdida,
}
