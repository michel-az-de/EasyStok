namespace EasyStock.Application.Ports.Output.Storage;

public interface IFileStorage
{
    Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFileInfo>> ListAsync(string bucketPath, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAsync(string storageKey, CancellationToken cancellationToken = default);

    // ── Novos métodos para relatórios assíncronos (PR-C0) ─────────────────────

    /// <summary>
    /// Abre um stream para upload streaming (sem carregar tudo em memória).
    /// Ideal para relatórios grandes — dados são enviados em chunks conforme escritos.
    /// </summary>
    Task<Stream> OpenUploadStreamAsync(string storageKey, string contentType, CancellationToken ct);

    /// <summary>Download via stream (sem carregar tudo em memória).</summary>
    Task<Stream> DownloadStreamAsync(string storageKey, CancellationToken ct);

    /// <summary>
    /// Gera URL pre-signed para download direto pelo cliente (sem passar pela API).
    /// TTL curto (ex: 15 min) — regenerar em cada GET do run.
    /// </summary>
    Task<Uri> CreatePreSignedDownloadUrlAsync(
        string storageKey,
        TimeSpan ttl,
        string downloadFileName,
        CancellationToken ct);

    /// <summary>Verifica se o objeto existe no storage sem baixar.</summary>
    Task<bool> ExistsAsync(string storageKey, CancellationToken ct);
}

/// <param name="PrivateAllowedMimeTypes">
/// Allowlist própria de um upload privado (ex.: mídia recebida no atendimento, que inclui áudio e
/// vídeo). Ignorada quando <paramref name="IsPublic"/>: o público sempre usa a whitelist conservadora
/// de <c>UploadSecurityValidator.AllowedMimeTypes</c>.
/// </param>
public sealed record FileUploadRequest(
    string BucketPath,
    string FileName,
    string ContentType,
    byte[] Content,
    bool IsPublic = true,
    IReadOnlySet<string>? PrivateAllowedMimeTypes = null);

public sealed record StoredFileResult(
    string StorageKey,
    string Url,
    string ContentType,
    long Size);

public sealed record StoredFileInfo(
    string StorageKey,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModified);
