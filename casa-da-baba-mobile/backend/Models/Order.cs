using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Mobile.Models;

/// <summary>
/// Pedido feito pelo app. Estados: aguardando -> preparando -> pronto -> entregue.
/// Cancelado e estado lateral a partir de qualquer um.
/// Ao virar "pronto", o app desconta Stock dos produtos (regra de reserva).
/// Ao cancelar de "pronto" ou "entregue", devolve Stock.
/// </summary>
[Table("mobile_orders")]
public class Order
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Id do cliente. Null para pedidos avulsos.</summary>
    [MaxLength(64)]
    public string? ClientId { get; set; }

    /// <summary>Nome do cliente congelado no momento do pedido (historico).</summary>
    [Required, MaxLength(120)]
    public string ClientSnapshotName { get; set; } = default!;

    /// <summary>Referencia do cliente congelada no momento do pedido (ex: "apto 204").</summary>
    [MaxLength(255)]
    public string? ClientSnapshotRef { get; set; }

    /// <summary>Observacao livre do pedido.</summary>
    public string? Notes { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal Total { get; set; }

    /// <summary>"aguardando", "preparando", "pronto", "entregue", "cancelado".</summary>
    [Required, MaxLength(16)]
    public string Status { get; set; } = "aguardando";

    /// <summary>
    /// Data agendada de entrega (apenas data, sem hora). Null = pedido pronta-entrega.
    /// Usado pelo dashboard "Encomendas de hoje" e pela cobertura de produção.
    /// </summary>
    [Column(TypeName = "date")]
    public DateTime? ScheduledFor { get; set; }

    /// <summary>Janela de entrega: "manha", "tarde" ou "noite".</summary>
    [MaxLength(16)]
    public string? ScheduledWindow { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? LastDeviceId { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}

[Table("mobile_order_items")]
public class OrderItem
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(64)]
    public string OrderId { get; set; } = default!;

    [Required, MaxLength(64)]
    public string ProductId { get; set; } = default!;

    /// <summary>Snapshot do nome do produto no momento do pedido.</summary>
    [Required, MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(16)]
    public string? Emoji { get; set; }

    [MaxLength(32)]
    public string? Unit { get; set; }

    public int Qty { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal UnitPrice { get; set; }

    [ForeignKey(nameof(OrderId))]
    public Order? Order { get; set; }
}
