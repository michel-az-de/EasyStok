using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Lote de produção. Cada turno de produção vira um Batch com os itens produzidos.
/// Criação é imutável: não há update após a criação (re-envio é ignorado).
/// Ao criar, incrementa o Stock dos produtos.
/// </summary>
[Table("mobile_batches")]
public class Batch
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Código simbólico (ex: "LASA-220426-001").</summary>
    [Required, MaxLength(32)]
    public string Code { get; set; } = default!;

    /// <summary>Foto do lote inteiro (data URL base64 ou caminho de blob).</summary>
    [Column("batch_photo")]
    public string? BatchPhoto { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_device_id"), MaxLength(64)]
    public string? LastDeviceId { get; set; }

    [Column("last_operator_name"), MaxLength(64)]
    public string? LastOperatorName { get; set; }

    public List<BatchItem> Items { get; set; } = new();
}

[Table("mobile_batch_items")]
public class BatchItem
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(64), Column("batch_id")]
    public string BatchId { get; set; } = default!;

    [Required, MaxLength(64), Column("product_id")]
    public string ProductId { get; set; } = default!;

    [Required, MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(16)]
    public string? Emoji { get; set; }

    [MaxLength(32)]
    public string? Unit { get; set; }

    public int Qty { get; set; }

    /// <summary>Foto do item individual (opcional).</summary>
    public string? Photo { get; set; }

    [ForeignKey(nameof(BatchId))]
    public Batch? Batch { get; set; }
}
