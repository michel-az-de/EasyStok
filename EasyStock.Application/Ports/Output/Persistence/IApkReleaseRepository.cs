namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistência de releases de APK (back-office Admin). Encapsula a despromoção
/// das releases ativas + insert do novo blob e a listagem, que antes viviam
/// direto no AdminApkReleaseController (F7).
/// </summary>
public interface IApkReleaseRepository
{
    /// <summary>
    /// Despromove as releases ativas do mesmo (AppId, IsCanaryOnly) e persiste a nova
    /// como ativa. Mantém histórico (registros antigos só viram IsActive=false).
    /// </summary>
    Task<ApkReleaseCriada> PublicarAsync(ApkReleaseNova nova, CancellationToken ct = default);

    Task<IReadOnlyList<ApkReleaseListItem>> ListarAsync(string appId, int limite, CancellationToken ct = default);
}

public sealed record ApkReleaseNova(
    string AppId,
    string Version,
    string Sha256,
    string? ReleaseNotes,
    byte[] FileContent,
    bool IsCanaryOnly);

public sealed record ApkReleaseCriada(
    Guid Id,
    string Version,
    string Sha256,
    long FileSizeBytes,
    bool IsCanaryOnly);

public sealed record ApkReleaseListItem(
    Guid Id,
    string Version,
    string Sha256,
    string? ReleaseNotes,
    long FileSizeBytes,
    bool IsCanaryOnly,
    bool IsActive,
    DateTime CriadoEm);
