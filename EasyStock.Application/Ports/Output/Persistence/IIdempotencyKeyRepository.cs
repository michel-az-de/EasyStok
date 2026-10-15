namespace EasyStock.Application.Ports.Output.Persistence;

public interface IIdempotencyKeyRepository
{
    /// <summary>
    /// Tenta RESERVAR a chave via INSERT com Status=Pendente (issue 917 Fase B). Se colidir
    /// com uma linha já existente (ativa, mesma tripla Key+EmpresaId+MetodoRecurso — via o
    /// índice único <c>ux_idempotency_key_empresa_recurso</c>), retorna
    /// <c>(existente, Reservado: false)</c> em vez de lançar. O caller decide replay/409/CAS
    /// a partir do <see cref="Domain.Entities.IdempotencyKey.Status"/> da linha existente.
    /// Retorna <c>(nova, Reservado: true)</c> quando não há colisão — o caller pode prosseguir.
    /// </summary>
    Task<(Domain.Entities.IdempotencyKey Entry, bool Reservado)> TryBeginAsync(
        string key, Guid empresaId, string metodoRecurso, string? payloadHash, TimeSpan ttl, CancellationToken ct = default);

    /// <summary>
    /// CAS: assume uma chave Pendente cujo lease expirou (processo morreu sem finalizar),
    /// atualizando <c>LockedAtUtc</c> só se ainda estiver no valor esperado. Retorna true se
    /// o takeover teve sucesso (nenhum outro concorrente já assumiu antes).
    /// </summary>
    Task<bool> TryTakeoverAsync(Guid id, DateTime lockedAtUtcEsperado, DateTime novoLock, CancellationToken ct = default);

    /// <summary>
    /// CAS: reabre uma chave Falhou para Pendente (o cliente corrigiu o payload e tenta de
    /// novo com a MESMA chave). Retorna true se a reabertura teve sucesso.
    /// </summary>
    Task<bool> TryReabrirAsync(Guid id, string? novoPayloadHash, DateTime novoLock, CancellationToken ct = default);

    /// <summary>Marca a chave como concluída (2xx) com a resposta cacheada para replay.</summary>
    Task MarcarConcluidoAsync(Guid id, int httpStatus, string? respostaJson, CancellationToken ct = default);

    /// <summary>Marca a chave como falha (4xx/5xx ou exceção não tratada) — permite reenvio
    /// com a mesma chave via <see cref="TryReabrirAsync"/>.</summary>
    Task MarcarFalhouAsync(Guid id, CancellationToken ct = default);

    /// <summary>Remove chaves expiradas (executado por job de limpeza).</summary>
    Task<int> CleanupExpiredAsync(DateTime referencia, CancellationToken ct = default);
}
