using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Pedido do app. Estados: aguardando → preparando → pronto → entregue.
/// "Cancelado" pode ser atingido a partir de qualquer estado. Ao virar "pronto"
/// ou "entregue", o Stock dos produtos é descontado (regra de reserva).
/// Cancelar de "pronto"/"entregue" devolve ao estoque.
/// </summary>
[Table("mobile_orders")]
public class Order
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Id do cliente. Null para pedidos avulsos.</summary>
    [Column("client_id"), MaxLength(64)]
    public string? ClientId { get; set; }

    /// <summary>Nome do cliente congelado no momento do pedido (histórico).</summary>
    [Required, MaxLength(120), Column("client_snapshot_name")]
    public string ClientSnapshotName { get; set; } = default!;

    /// <summary>Referência do cliente congelada no momento do pedido (ex: "apto 204").</summary>
    [Column("client_snapshot_ref"), MaxLength(255)]
    public string? ClientSnapshotRef { get; set; }

    /// <summary>Observação livre do pedido.</summary>
    public string? Notes { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal Total { get; set; }

    /// <summary>"aguardando", "preparando", "pronto", "entregue", "cancelado".</summary>
    [Required, MaxLength(16)]
    public string Status { get; set; } = "aguardando";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_device_id"), MaxLength(64)]
    public string? LastDeviceId { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

[Table("mobile_order_items")]
public class OrderItem
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(64), Column("order_id")]
    public string OrderId { get; set; } = default!;

    [Required, MaxLength(64), Column("product_id")]
    public string ProductId { get; set; } = default!;

    /// <summary>Snapshot do nome do produto no momento do pedido.</summary>
    [Required, MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(16)]
    public string? Emoji { get; set; }

    [MaxLength(32)]
    public string? Unit { get; set; }

    public int Qty { get; set; }

    [Column("unit_price", TypeName = "numeric(10,2)")]
    public decimal UnitPrice { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
}
