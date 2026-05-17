using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Mobile.Models;

/// <summary>
/// Lote de producao. Cada vez que a Thati ou o Felipe batem um turno de producao,
/// vira um Batch com os itens produzidos, foto opcional e codigo simbolico.
/// Ao criar um Batch, o estoque dos produtos e incrementado.
/// </summary>
[Table("mobile_batches")]
public class Batch
{
    [Key]
    [MaxLength(64)]
    public string Id { get; set; } = default!;

    /// <summary>Codigo simbolico (ex: "LASA-220426-001").</summary>
    [Required, MaxLength(32)]
    public string Code { get; set; } = default!;

    /// <summary>Foto do lote inteiro (data URL base64 ou caminho de arquivo no blob).</summary>
    public string? BatchPhoto { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(64)]
    public string? LastDeviceId { get; set; }

    public List<BatchItem> Items { get; set; } = new();
}

[Table("mobile_batch_items")]
public class BatchItem
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(64)]
    public string BatchId { get; set; } = default!;

    [Required, MaxLength(64)]
    public string ProductId { get; set; } = default!;

    [Required, MaxLength(120)]
    public string Name { get; set; } = default!;

    [MaxLength(16)]
    public string? Emoji { get; set; }

    [MaxLength(32)]
    public string? Unit { get; set; }

    public int Qty { get; set; }

    /// <summary>
    /// Gramagem real produzida (quando o produto é vendido por peso). Nullable
    /// porque produtos antigos vão continuar sem peso e itens contados em
    /// unidades não preenchem. A UI consolidada soma WeightGrams quando o
    /// produto for do tipo gramas; senão soma Qty.
    /// </summary>
    public int? WeightGrams { get; set; }

    /// <summary>Foto do item individual (opcional).</summary>
    public string? Photo { get; set; }

    [ForeignKey(nameof(BatchId))]
    public Batch? Batch { get; set; }
}
