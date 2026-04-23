using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Produto do catálogo do app mobile (Casa da Baba). Espelha o cardápio.
/// Pode ser criado ad-hoc pelo app (<see cref="IsCustom"/>=true) e revisado
/// depois no EasyStock web (<see cref="IsApproved"/>).
/// </summary>
[Table("mobile_products")]
public class Product
{
    /// <summary>Id textual vindo do app (ex: "lasanha", "custom-1714...").</summary>
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [Required, MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(16)]
    public string? Emoji { get; set; }

    /// <summary>"massa", "molho" ou "extra".</summary>
    [Required, MaxLength(16)]
    public string Category { get; set; } = "extra";

    [MaxLength(32)]
    public string? Unit { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal? Price { get; set; }

    /// <summary>Quantidade atual disponível em estoque (unidades).</summary>
    public int Stock { get; set; }

    /// <summary>True se foi criado ad-hoc pelo app e ainda não foi revisado.</summary>
    [Column("is_custom")]
    public bool IsCustom { get; set; }

    /// <summary>Flag opcional pra marcar produtos que passaram por revisão no ERP.</summary>
    [Column("is_approved")]
    public bool IsApproved { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Id do device que criou/alterou por último. Para reconciliação de conflito.</summary>
    [Column("last_device_id"), MaxLength(64)]
    public string? LastDeviceId { get; set; }
}
