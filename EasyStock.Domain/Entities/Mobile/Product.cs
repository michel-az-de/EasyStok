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

    /// <summary>Nome do operador (informado no PWA) que fez a última alteração. Auditoria.</summary>
    [Column("last_operator_name"), MaxLength(64)]
    public string? LastOperatorName { get; set; }

    /// <summary>SKU pra etiqueta de produção (prefixo do código de barras). Ex: "TALH".</summary>
    [Column("sku"), MaxLength(32)]
    public string? Sku { get; set; }

    /// <summary>Peso default em gramas — pré-preenche tela de revisão de produção.</summary>
    [Column("default_weight_g")]
    public int? DefaultWeightG { get; set; }

    /// <summary>Validade default em dias — pré-preenche tela de revisão de produção.</summary>
    [Column("default_validity_days")]
    public int? DefaultValidityDays { get; set; }

    /// <summary>
    /// Empresa proprietária do registro (multi-tenant Onda 1). Nullable
    /// para compat com registros pré-Onda-1 (devices ainda não pareados).
    /// SyncController preenche automaticamente a partir do device autenticado.
    /// </summary>
    [Column("empresa_id")]
    public Guid? EmpresaId { get; set; }

    /// <summary>Loja onde o registro vive dentro da empresa.</summary>
    [Column("loja_id")]
    public Guid? LojaId { get; set; }

    /// <summary>
    /// Onda 2 — link opcional pra <c>Produto</c> do ERP. NULL = só vive
    /// no mobile (custom criado no app, ainda não revisado). Quando o
    /// operador aprova/linka pelo painel /produtos-mobile, este campo
    /// é populado e o stock passa a reconciliar com itens_estoque ERP.
    /// </summary>
    [Column("erp_product_id")]
    public Guid? ErpProductId { get; set; }

    /// <summary>Quando o produto foi aprovado/linkado no painel web.</summary>
    [Column("approved_at")]
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Usuário do EasyStock que aprovou/linkou. Audit trail.</summary>
    [Column("approved_by_user_id")]
    public Guid? ApprovedByUserId { get; set; }
}
