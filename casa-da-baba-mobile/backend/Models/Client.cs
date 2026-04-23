using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Mobile.Models;

/// <summary>
/// Cliente do Casa da Baba. Pode ser morador do predio (Apt preenchido),
/// externo (Address preenchido) ou avulso (nao cadastrado - nem chega aqui).
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

    public DateTime LastOrder { get; set; } = DateTime.UtcNow;
    public int OrderCount { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? LastDeviceId { get; set; }
}
