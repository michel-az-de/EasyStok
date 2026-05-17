using EasyStock.Domain.Financeiro;

namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Porta de persistencia para o agregado <see cref="Lancamento"/> (AR/AP).
/// <see cref="GetWithLockAsync"/> e o metodo principal para fluxos de baixa — usa
/// lock pessimista para serializar concorrencia; os demais metodos sao leitura simples.
/// </summary>
public interface ILancamentoRepository
{
    Task<Lancamento?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    /// <summary>
    /// Carrega o lancamento adquirindo lock pessimista (FOR UPDATE) na linha.
    /// Serializa baixas concorrentes — duas requisicoes simultaneas para o mesmo
    /// lancamento ficam em fila no nivel do banco. Exige transacao explicita
    /// aberta (use <c>IUnitOfWork.ExecuteInTransactionAsync</c>); fora dela o
    /// lock e liberado imediatamente e a serializacao e perdida.
    /// </summary>
    Task<Lancamento?> GetWithLockAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task AddAsync(Lancamento lancamento, CancellationToken ct = default);
    Task UpdateAsync(Lancamento lancamento, CancellationToken ct = default);
}
