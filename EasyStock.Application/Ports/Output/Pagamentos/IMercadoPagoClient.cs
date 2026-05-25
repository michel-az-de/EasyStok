namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Porta de saída para o gateway MercadoPago (ADR-0005).
/// Implementações: <c>MercadoPagoClient</c> (HTTP direto, sem SDK estático)
/// e <c>StubMercadoPagoClient</c> (ambiente Development).
/// </summary>
public interface IMercadoPagoClient
{
    /// <summary>
    /// Cria uma Preference MP e retorna a URL de checkout (init_point).
    /// Timeout de 5 s definido na implementação concreta (ADR-0005).
    /// </summary>
    Task<PreferenceCriadaResult> CriarPreferenceAsync(
        CriarPreferenceCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Busca detalhes de um pagamento (<c>GET /v1/payments/{id}</c>) — fonte da
    /// verdade do <c>ProcessarWebhookMpUseCase</c> (ADR-0006). Nunca confiamos
    /// no payload do webhook em si: sempre consultamos esta API com o
    /// <paramref name="paymentId"/> recebido.
    /// </summary>
    Task<MpPaymentDetailsDto> GetPaymentAsync(
        string paymentId,
        CancellationToken ct = default);
}
