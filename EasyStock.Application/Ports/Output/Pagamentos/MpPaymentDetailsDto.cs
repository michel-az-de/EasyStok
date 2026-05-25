namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Detalhes do pagamento retornados pela API <c>GET /v1/payments/{id}</c> do MercadoPago.
/// Fonte da verdade no fluxo de webhook (ADR-0006 §Process): nunca confiamos no payload
/// do webhook em si — sempre validamos chamando essa API.
///
/// <para>
/// <strong>Status canônicos do MP:</strong>
/// <c>approved</c>, <c>rejected</c>, <c>cancelled</c>, <c>pending</c>, <c>in_process</c>,
/// <c>refunded</c>, <c>charged_back</c>.
/// </para>
/// </summary>
/// <param name="PaymentId">ID do pagamento no MP (mesma chave do webhook).</param>
/// <param name="Status">Status canônico (lowercase, conforme MP).</param>
/// <param name="StatusDetail">Subcausa específica (ex: <c>cc_rejected_insufficient_amount</c>). Opcional.</param>
/// <param name="ExternalReference">
/// Referência externa que enviamos ao criar a Preference — para storefront, é o
/// <c>PedidoId</c> em formato string GUID.
/// </param>
/// <param name="Amount">Valor total cobrado em BRL. Para conferência.</param>
public sealed record MpPaymentDetailsDto(
    string PaymentId,
    string Status,
    string? StatusDetail,
    string? ExternalReference,
    decimal Amount);
