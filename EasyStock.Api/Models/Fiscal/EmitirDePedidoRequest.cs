using System.ComponentModel.DataAnnotations;

namespace EasyStock.Api.Models.Fiscal;

/// <summary>
/// Payload do endpoint <c>POST /api/notas-fiscais/emitir-de-pedido</c> usado
/// pelo admin Web para emitir uma NFC-e baseada em um pedido ja existente.
/// O backend resolve emitente (config fiscal) e itens (PedidoItem) — caller
/// passa apenas a referencia do pedido e opcionalmente dados do destinatario.
/// </summary>
public sealed class EmitirDePedidoRequest
{
    [Required]
    public Guid PedidoId { get; set; }

    /// <summary>Opcional — backend gera se nao informado.</summary>
    [StringLength(80)]
    public string? IdempotencyKey { get; set; }

    [StringLength(14, MinimumLength = 11)]
    public string? DestinatarioCpf { get; set; }

    [StringLength(120)]
    public string? DestinatarioNome { get; set; }

    [EmailAddress, StringLength(120)]
    public string? DestinatarioEmail { get; set; }
}
