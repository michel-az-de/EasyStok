using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Domain.Entities.Pagamentos;

/// <summary>
/// Audit trail de transicoes de estado de um <see cref="PaymentAttempt"/>.
/// Apenas leitura apos persistir — eventos sao append-only.
///
/// <para>
/// <b>Quem grava</b>: o <c>IPagamentoOrchestrator</c> ao mudar status do
/// attempt; webhook handlers ao confirmar; reconciliador (P1) ao consultar
/// gateway; admin ao cancelar manualmente.
/// </para>
///
/// <para>
/// <b>Cascade</b>: deletar o <see cref="PaymentAttempt"/> apaga eventos
/// (relacao mae→filho com cascade fisico).
/// </para>
/// </summary>
public class PaymentAttemptEvent
{
    public Guid Id { get; set; }
    public Guid PaymentAttemptId { get; set; }
    public Guid EmpresaId { get; set; }

    /// <summary><c>null</c> = primeiro evento (criacao do attempt).</summary>
    public StatusPaymentAttempt? FromStatus { get; set; }

    public StatusPaymentAttempt ToStatus { get; set; }

    /// <summary>
    /// "iniciado" | "webhook_recebido" | "timeout_reconcile" | "gateway_5xx_retry"
    /// | "fallback_to_X" | "manual_admin" | "circuit_open" | "client_idempotency_replay" | etc.
    /// </summary>
    public string Motivo { get; set; } = null!;

    /// <summary>Resposta crua do gateway (jsonb) ou contexto adicional.</summary>
    public string? GatewayResponseJson { get; set; }

    /// <summary>UserId quando a transicao foi acionada manualmente por admin.</summary>
    public Guid? UserId { get; set; }

    public DateTime OcorridoEm { get; set; }

    public string? CorrelationId { get; set; }

    public PaymentAttempt? PaymentAttempt { get; set; }

    private PaymentAttemptEvent() { }

    public static PaymentAttemptEvent Registrar(
        Guid paymentAttemptId,
        Guid empresaId,
        StatusPaymentAttempt? fromStatus,
        StatusPaymentAttempt toStatus,
        string motivo,
        string? gatewayResponseJson = null,
        Guid? userId = null,
        string? correlationId = null)
    {
        return new PaymentAttemptEvent
        {
            Id = Guid.NewGuid(),
            PaymentAttemptId = paymentAttemptId,
            EmpresaId = empresaId,
            FromStatus = fromStatus,
            ToStatus = toStatus,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? "no-reason" : motivo.Trim(),
            GatewayResponseJson = string.IsNullOrWhiteSpace(gatewayResponseJson) ? null : gatewayResponseJson,
            UserId = userId,
            OcorridoEm = DateTime.UtcNow,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? null : correlationId.Trim()
        };
    }
}
