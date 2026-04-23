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
}
