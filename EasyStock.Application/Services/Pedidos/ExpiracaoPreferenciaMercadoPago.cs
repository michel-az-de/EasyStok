using EasyStock.Application.Ports.Output.Pagamentos;

namespace EasyStock.Application.Services.Pedidos;

/// <summary>
/// #1717-8: o link substituído (troca de forma, reemissão por valor, quitação manual) para de aceitar pagamento no
/// Mercado Pago (<c>PUT checkout/preferences/{id}</c>). Roda sempre depois do commit real e é best-effort: a falha
/// só loga, e um pagamento que ainda chegue pelo link antigo é tratado pela confirmação (dinheiro recebido vence).
/// O cliente do provedor é resolvido só quando há preferência a expirar: host sem pagamento não precisa dele.
/// </summary>
public sealed class ExpiracaoPreferenciaMercadoPago(
    Lazy<IMercadoPagoClient> mercadoPago,
    TimeProvider relogio,
    ILogger<ExpiracaoPreferenciaMercadoPago> logger)
{
    public async Task ExpirarAsync(IEnumerable<string> preferencias, CancellationToken ct)
    {
        foreach (var preferencia in preferencias.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.Ordinal))
            await ExpirarAsync(() => mercadoPago.Value, preferencia, relogio.GetUtcNow().UtcDateTime, logger, ct);
    }

    /// <summary>Uma preferência; compartilhado com os use cases que já recebem o cliente do provedor.</summary>
    public static async Task ExpirarAsync(Func<IMercadoPagoClient> mercadoPago, string preferencia, DateTime agora,
        ILogger logger, CancellationToken ct)
    {
        try
        {
            await mercadoPago().ExpirarPreferenciaAsync(preferencia, agora, ct);
        }
        // Timeout do HttpClient chega como TaskCanceledException sem o ct cancelado: também é só log, o commit já houve.
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Falha ao expirar a preferencia substituida {Preferencia} no Mercado Pago", preferencia);
        }
    }
}
