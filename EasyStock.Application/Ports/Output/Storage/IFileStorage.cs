namespace EasyStock.Application.Ports.Output.Storage;

public interface IFileStorage
{
    Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StoredFileInfo>> ListAsync(string bucketPath, CancellationToken cancellationToken = default);
    Task<byte[]> DownloadAsync(string storageKey, CancellationToken cancellationToken = default);
}

public sealed record FileUploadRequest(
    string BucketPath,
    string FileName,
    string ContentType,
    byte[] Content,
    bool IsPublic = true);

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
