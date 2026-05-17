using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Mobile.Models;

/// <summary>
/// Produto do catalogo do app mobile. Espelha o que aparece no cardapio
/// do Casa da Baba. Pode ser criado na hora pelo app (Custom=true) e depois
/// revisado/promovido pelo EasyStock web (Approved=true).
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

    /// <summary>Quantidade atual disponivel em estoque (unidades).</summary>
    public int Stock { get; set; }

    /// <summary>
    /// Unidade padrão da produção: "gramas" ou "unidades". Quando "gramas",
    /// a UI de cadastro de produção pede gramagem e a visão consolidada
    /// agrega WeightGrams; quando "unidades", agrega Qty.
    /// </summary>
    [MaxLength(16)]
    public string? DefaultUnit { get; set; }

    /// <summary>Gramagem padrão sugerida ao cadastrar produção (ex: 300).</summary>
    public int? DefaultGrams { get; set; }

    /// <summary>True se foi criado ad-hoc pelo app e ainda nao foi revisado.</summary>
    public bool IsCustom { get; set; }

    /// <summary>Flag opcional pra marcar produtos que passaram por revisao no ERP.</summary>
    public bool IsApproved { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Id do device que criou/alterou por ultimo. Pra reconciliacao de conflito.</summary>
    [MaxLength(64)]
    public string? LastDeviceId { get; set; }
}
