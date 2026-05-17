using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EasyStock.Domain.Entities.Mobile;

/// <summary>
/// Release publicada do APK Casa da Baba (e futuramente outros apps mobile).
/// O arquivo .apk e armazenado como bytea no Postgres — Render free tier nao
/// tem disco persistente e pra 1 cliente (Casa da Baba) o overhead e
/// aceitavel. Quando escalar pra multi-tenant pesado, migrar pra Azure Blob.
///
/// Fluxo:
///   1. CI build-casadababa-release.yml produz .apk + sha256 + versao
///   2. Workflow chama POST /api/admin/apk-release com multipart
///   3. AdminApkReleaseController persiste em apk_releases, marca IsActive
///   4. APK em campo (CapacitorUpdater plugin) pinga GET /api/mobile/apk/manifest
///   5. Plugin compara versao local com manifest, baixa de /api/mobile/apk/download
///      se diferente, aplica update silencioso ou pergunta ao usuario
///
/// Canary: marcar IsCanaryOnly=true faz manifest retornar essa versao APENAS
/// pra devices com MobileDevice.IsCanary=true. Resto da frota fica na ultima
/// versao Active sem flag canary.
/// </summary>
[Table("apk_releases")]
public class ApkRelease
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>Identificador do app (futuro multi-app). "casa-da-baba" por default.</summary>
    [Required, MaxLength(64)]
    [Column("app_id")]
    public string AppId { get; set; } = "casa-da-baba";

    /// <summary>Semver. Ex: "1.0.3". CapacitorUpdater compara lexicograficamente — use zero-pad.</summary>
    [Required, MaxLength(32)]
    [Column("version")]
    public string Version { get; set; } = default!;

    /// <summary>SHA-256 do arquivo .apk em hex lowercase. Plugin valida antes de aplicar.</summary>
    [Required, MaxLength(64)]
    [Column("sha256")]
    public string Sha256 { get; set; } = default!;

    [MaxLength(2048)]
    [Column("release_notes")]
    public string? ReleaseNotes { get; set; }

    /// <summary>Conteudo binario do .apk. Streamed pelo endpoint download.</summary>
    [Required]
    [Column("file_content")]
    public byte[] FileContent { get; set; } = default!;

    [Column("file_size_bytes")]
    public long FileSizeBytes { get; set; }

    /// <summary>True = manifest so retorna essa versao pra devices com IsCanary=true.</summary>
    [Column("is_canary_only")]
    public bool IsCanaryOnly { get; set; }

    /// <summary>
    /// True = release atualmente promovida. Apenas 1 release pode ser Active=true
    /// por (AppId, IsCanaryOnly) — o AdminApkReleaseController despromove a anterior.
    /// </summary>
    [Column("is_active")]
    public bool IsActive { get; set; }

    [Column("criado_em")]
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    [Column("criado_por_id")]
    public Guid? CriadoPorId { get; set; }
}
