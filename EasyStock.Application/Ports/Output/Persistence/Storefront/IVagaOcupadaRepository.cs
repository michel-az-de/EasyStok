using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Ports.Output.Persistence.Storefront;

/// <summary>
/// Repo de <see cref="VagaOcupada"/>. <see cref="OcuparAsync"/> faz INSERT atômico
/// condicionado a COUNT(*) &lt; capacidade da janela (ADR-0014 §Solução 1) — lança
/// <see cref="EasyStock.Domain.Exceptions.Storefront.JanelaSemVagasException"/> se 0 rows.
/// </summary>
public interface IVagaOcupadaRepository
{
    Task<VagaOcupada?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// INSERT atômico: cria <see cref="VagaOcupada"/> se ainda houver capacidade,
    /// senão lança <see cref="EasyStock.Domain.Exceptions.Storefront.JanelaSemVagasException"/>.
    /// Retorna a entity criada.
    /// </summary>
    Task<VagaOcupada> OcuparAsync(
        Guid janelaEntregaId,
        DateOnly dataEntrega,
        Guid pedidoId,
        CancellationToken ct = default);

    /// <summary>
    /// Marca como liberada (idempotente — ADR-0014 §Solução 3). No-op se não encontrar
    /// vaga ativa pro pedido. Retorna true se liberou nesta chamada.
    /// </summary>
    Task<bool> LiberarPorPedidoAsync(Guid pedidoId, string motivo, CancellationToken ct = default);

    /// <summary>Conta vagas ativas para uma janela em uma data específica.</summary>
    Task<int> ContarAtivasPorJanelaDataAsync(Guid janelaEntregaId, DateOnly dataEntrega, CancellationToken ct = default);

    /// <summary>
    /// Vagas órfãs: liberado_em IS NULL mas pedido inexistente ou cancelado/entregue
    /// (cinto+suspensório — ADR-0014 §Solução 4).
    /// </summary>
    Task<IReadOnlyList<VagaOcupada>> GetOrfasAsync(CancellationToken ct = default);
}
