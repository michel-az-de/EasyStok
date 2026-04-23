using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Lançamento manual de caixa. Pode ser despesa (compra de insumo) ou
/// entrada extra (pagamento por fora). Vendas de pedidos NÃO geram CashEntry —
/// são totalizadas via <c>Order.Status = "entregue"</c>.
/// Imutável após criação.
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

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_device_id"), MaxLength(64)]
    public string? LastDeviceId { get; set; }
}
