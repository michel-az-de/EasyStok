namespace EasyStock.Domain.Entities;

/// <summary>
/// F10-C-3 — Idempotency server-side pra mutations mobile.
///
/// PK composta (MutationId, DeviceId): reenvio da mesma mutation pelo mesmo
/// device retorna a mesma resposta sem reprocessar.
///
/// Principio #8: Server idempotente por (MutationId, DeviceId).
/// Principio #3: Atomicidade por mutation, nao por batch.
/// </summary>
public class MobileProcessedMutation
{
    /// <summary>UUID v4 gerado no device (ex: "mut_550e8400-e29b-41d4-a716-446655440000").</summary>
    public string MutationId { get; set; } = null!;

    /// <summary>DeviceId do envelope da mutation (estavel, sobrevive re-pair).</summary>
    public string DeviceId { get; set; } = null!;

    /// <summary>Tenant pra filtragem e retention.</summary>
    public Guid EmpresaId { get; set; }

    /// <summary>"accepted" | "rejected:conflict" | "rejected:validation" | "rejected:auth" etc.</summary>
    public string Outcome { get; set; } = null!;

    /// <summary>
    /// JSON com metadata da resposta (acceptedIds, rejectedReason, etc.) SEM PII.
    /// Permite replay da mesma resposta sem reprocessar.
    /// </summary>
    public string? ResponseMeta { get; set; }

    public DateTime CriadoEm { get; set; }
}
