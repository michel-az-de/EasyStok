namespace EasyStock.Domain.Enums;

/// <summary>
/// Estado de uma <see cref="Entities.IdempotencyKey"/> ao longo do request que a criou
/// (issue 917 Fase B). Substitui o modelo antigo "só existe se 2xx completou" por um
/// registro INSERT-first: a chave é reservada ANTES do handler rodar, fechando o
/// check-then-act (TOCTOU) do middleware original — dois requests concorrentes com a
/// MESMA chave não podem mais passar ambos pelo <c>GetActiveAsync</c> antes de qualquer
/// um persistir, porque agora o INSERT em si é o ponto de serialização (via o índice
/// único <c>ux_idempotency_key_empresa_recurso</c>).
/// </summary>
public enum StatusIdempotencyKey
{
    /// <summary>Reservada; o handler ainda está processando (ou morreu sem finalizar —
    /// ver <see cref="Entities.IdempotencyKey.LockedAtUtc"/> para o lease de takeover).</summary>
    Pendente = 0,

    /// <summary>Handler concluiu com 2xx; <see cref="Entities.IdempotencyKey.RespostaJson"/>
    /// contém a resposta cacheada para replay.</summary>
    Concluido = 1,

    /// <summary>Handler terminou com 4xx/5xx ou lançou exceção não tratada. Permite reenvio
    /// com a MESMA chave (o cliente corrigiu o payload e tenta de novo) via CAS Falhou→Pendente.</summary>
    Falhou = 2,
}
