using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Cliente do Casa da Baba. Pode ser morador do prédio (Apt preenchido)
/// ou externo (Address preenchido).
/// </summary>
[Table("mobile_clients")]
public class Client
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    [Required, MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(32)]
    public string? Apt { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(32)]
    public string? Phone { get; set; }

    [Column("last_order")]
    public DateTime LastOrder { get; set; } = DateTime.UtcNow;

    [Column("order_count")]
    public int OrderCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_device_id"), MaxLength(64)]
    public string? LastDeviceId { get; set; }

    [Column("last_operator_name"), MaxLength(64)]
    public string? LastOperatorName { get; set; }

    /// <summary>Multi-tenant (Onda 1). Resolvido via device autenticado.</summary>
    [Column("empresa_id")]
    public Guid? EmpresaId { get; set; }

    [Column("loja_id")]
    public Guid? LojaId { get; set; }

    /// <summary>
    /// Onda P1 — link opcional pra <c>Cliente</c> do ERP. NULL = só vive
    /// no mobile, ainda não aprovado pelo gestor. Quando linkado, gestor
    /// pode editar pelo painel web e mudanças refletem no app via pull.
    /// </summary>
    [Column("erp_cliente_id")]
    public Guid? ErpClienteId { get; set; }

    [Column("approved_at")]
    public DateTime? ApprovedAt { get; set; }

    [Column("approved_by_user_id")]
    public Guid? ApprovedByUserId { get; set; }
}
