using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Snapshot do localStorage do PWA enviado pelo device pra recuperação
/// (Onda 8).
///
/// Caso de uso: operador perde aparelho ou reinstala app. Gestor abre
/// /dispositivos > Backups, baixa último JSON e cola em "Restaurar"
/// no Diagnóstico do app novo. Mantemos N por device (rotação no upload).
///
/// Não é fonte da verdade — sync continua sendo. É linha-da-vida pra
/// dados que vivem só no app (audit log local, lista de compras, layout
/// de tema/preferências).
/// </summary>
[Table("mobile_device_backups")]
public class DeviceBackup
{
    [Key]
    public Guid Id { get; set; }

    [Required, MaxLength(64)]
    [Column("device_id")]
    public string DeviceId { get; set; } = default!;

    [Column("empresa_id")]
    public Guid EmpresaId { get; set; }

    /// <summary>JSON com todas as chaves <c>cdb-*</c> do localStorage.</summary>
    [Required]
    [Column("snapshot_json")]
    public string SnapshotJson { get; set; } = default!;

    /// <summary>Tamanho do JSON pra alertas (snapshot &gt; 5MB sugere localStorage cheio).</summary>
    [Column("size_bytes")]
    public int SizeBytes { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Versão do bundle PWA quando o snapshot foi tirado.</summary>
    [MaxLength(64)]
    [Column("bundle_version")]
    public string? BundleVersion { get; set; }

    /// <summary>Operador ativo no momento do snapshot.</summary>
    [MaxLength(64)]
    [Column("operator_name")]
    public string? OperatorName { get; set; }

    /// <summary>"auto" pra backup diário automático; texto livre pra manual.</summary>
    [MaxLength(255)]
    public string? Note { get; set; }
}
