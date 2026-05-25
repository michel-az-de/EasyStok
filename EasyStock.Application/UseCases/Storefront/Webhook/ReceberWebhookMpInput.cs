namespace EasyStock.Application.UseCases.Storefront.Webhook;

/// <summary>
/// Input cru do <see cref="ReceberWebhookMpUseCase"/>. O controller lê o body
/// com <c>EnableBuffering</c> e empacota aqui — Application Layer NÃO tem
/// referência a <c>HttpContext</c>.
/// </summary>
/// <param name="PayloadRaw">Body completo bruto (bytes opacos) para validação HMAC.</param>
/// <param name="AssinaturaHmac">Header <c>Authorization</c> ou <c>X-Signature</c>.</param>
/// <param name="XRequestId">Header <c>x-request-id</c> do MP — usado como <c>eventoId</c> de dedup.</param>
public sealed record ReceberWebhookMpInput(
    byte[] PayloadRaw,
    string AssinaturaHmac,
    string XRequestId);
