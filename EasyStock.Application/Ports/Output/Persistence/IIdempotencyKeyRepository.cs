namespace EasyStock.Application.Ports.Output.Persistence;

public interface IIdempotencyKeyRepository
{
    /// <summary>
    /// Busca uma chave de idempotencia ativa (nao-expirada). Retorna null
    /// se nao existir ou ja tiver expirado.
    /// </summary>
    Task<IdempotencyKey?> GetActiveAsync(string key, Guid empresaId, string metodoRecurso, CancellationToken ct = default);

    /// <summary>
    /// Insere o registro. Se houver conflito por (Key, EmpresaId, MetodoRecurso)
    /// devolve a entrada existente. Implementacoes devem usar UPSERT atomico.
    /// </summary>
    Task<IdempotencyKey> SaveAsync(IdempotencyKey entry, CancellationToken ct = default);

    /// <summary>Remove chaves expiradas (executado por job de limpeza).</summary>
    Task<int> CleanupExpiredAsync(DateTime referencia, CancellationToken ct = default);
}
