using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Mobile.Models;

/// <summary>
/// Lancamento manual de caixa. Pode ser despesa (compra de insumo) ou
/// entrada extra (pagamento por fora, dinheiro recebido sem pedido).
/// Vendas de pedidos nao geram CashEntry - sao somadas direto via status=entregue.
/// </summary>
[Table("mobile_cash_entries")]
public class CashEntry
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>"expense" ou "income".</summary>
    [Required, MaxLength(16)]
    public string Type { get; set; } = "expense";

    [Column(TypeName = "numeric(10,2)")]
    public decimal Amount { get; set; }

    [Required, MaxLength(255)]
    public string Description { get; set; } = default!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? LastDeviceId { get; set; }
}
