namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Repositorio para idempotencia de webhooks recebidos. UNIQUE
/// (<c>Provedor</c>, <c>EventIdExterno</c>) garante que o mesmo evento nao
/// seja processado duas vezes — o segundo INSERT explode com violacao de
/// constraint, que o controller traduz em retorno 200 idempotente.
/// </summary>
public interface IWebhookRecebidoRepository
{
    /// <summary>
    /// Tenta registrar o evento. Retorna o registro persistido em caso de
    /// sucesso, ou null se ja existe um com mesmo (<c>Provedor</c>,
    /// <c>EventIdExterno</c>) — caso em que o caller NAO deve reprocessar.
    /// </summary>
    Task<WebhookRecebido?> TryRegistrarAsync(
        string provedor,
        string eventIdExterno,
        string rawBodyHash,
        CancellationToken ct = default);

    /// <summary>
    /// Carrega o registro existente por (<c>Provedor</c>, <c>EventIdExterno</c>).
    /// Usado quando <see cref="TryRegistrarAsync"/> retorna null (dedup): se a
    /// tentativa anterior NAO completou com sucesso (<c>ProcessadoEm</c> null ou
    /// <c>Sucesso</c> false), o caller deve REPROCESSAR em vez de responder 200
    /// idempotente — senao um webhook que falhou por causa transitoria (deadlock,
    /// timeout, deploy no meio) e perdido para sempre no retry do gateway (#787).
    /// </summary>
    Task<WebhookRecebido?> ObterAsync(string provedor, string eventIdExterno, CancellationToken ct = default);

    /// <summary>Marca processamento concluido (sucesso ou falha).</summary>
    Task MarcarProcessadoAsync(Guid id, bool sucesso, string? erro = null, CancellationToken ct = default);
}
