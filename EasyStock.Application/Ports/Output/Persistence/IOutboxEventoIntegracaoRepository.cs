using EasyStock.Domain.Integration;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Acesso à tabela <c>outbox_evento_integracao</c>. Repositório baixo-nível —
/// callers que publicam eventos devem usar
/// <c>IPublicadorEventoIntegracao</c>; consumers (dispatcher/admin) usam
/// este port direto.
/// </summary>
public interface IOutboxEventoIntegracaoRepository
{
    Task AddAsync(OutboxEventoIntegracao evento, CancellationToken ct = default);
    Task UpdateAsync(OutboxEventoIntegracao evento, CancellationToken ct = default);

    Task<OutboxEventoIntegracao?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Próximos N eventos pendentes do shard, prontos pra processar
    /// (ProximaTentativaEm &lt;= now). Ordem ascendente por CriadoEm
    /// (FIFO). Cross-tenant — uso interno do dispatcher; não respeita
    /// query filter multi-tenant.
    /// </summary>
    Task<IReadOnlyList<OutboxEventoIntegracao>> ProximosPendentesAsync(
        int shardKey,
        int max,
        CancellationToken ct = default);

    /// <summary>
    /// Lista eventos da empresa pra painel admin/observabilidade.
    /// Filtros opcionais por status e tipoEvento.
    /// </summary>
    Task<IReadOnlyList<OutboxEventoIntegracao>> ListarPorEmpresaAsync(
        Guid empresaId,
        StatusOutboxIntegracao? status = null,
        string? tipoEvento = null,
        int max = 100,
        CancellationToken ct = default);

    /// <summary>
    /// Idade do mais antigo evento pendente — métrica de health do
    /// dispatcher. Cross-tenant. Retorna 0 se nada pendente.
    /// </summary>
    Task<TimeSpan> LagDoMaisAntigoPendenteAsync(CancellationToken ct = default);
}
