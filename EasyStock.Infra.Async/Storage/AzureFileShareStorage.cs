using Azure.Storage;
using Azure.Storage.Files.Shares;
using Azure.Storage.Sas;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Async.Storage;

public sealed class AzureFileShareStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly AzureFileShareStorageOptions _opts = options.Value.AzureFileShare;

    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken ct = default)
    {
        // Fail-fast: valida filename (path traversal) e MIME antes de qualquer chamada Azure.
        var safeFileName = UploadSecurityValidator.SanitizeFileName(request.FileName);
        UploadSecurityValidator.EnsureValidMime(request.ContentType);

        if (string.IsNullOrWhiteSpace(_opts.ConnectionString) || _opts.ConnectionString.Contains("<"))
            throw new InvalidOperationException("Azure File Share não está configurado. Verifique appsettings.Production.json.");

        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient = serviceClient.GetShareClient(_opts.ShareName);
        await shareClient.CreateIfNotExistsAsync(cancellationToken: ct);

        // Ensure nested directories exist
        var segments = request.BucketPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        ShareDirectoryClient dirClient = shareClient.GetRootDirectoryClient();
        foreach (var segment in segments)
        {
            dirClient = dirClient.GetSubdirectoryClient(segment);
            await dirClient.CreateIfNotExistsAsync(cancellationToken: ct);
        }

        var fileClient = dirClient.GetFileClient(safeFileName);
        using var stream = new MemoryStream(request.Content);
        await fileClient.CreateAsync(stream.Length, cancellationToken: ct);
        await fileClient.UploadRangeAsync(new Azure.HttpRange(0, stream.Length), stream, cancellationToken: ct);

        var storageKey = (request.BucketPath.Trim('/') + "/" + safeFileName).Trim('/');
        var sasUri = GenerateSasUri(fileClient, storageKey);

        return new StoredFileResult(storageKey, sasUri, request.ContentType, request.Content.LongLength);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient = serviceClient.GetShareClient(_opts.ShareName);
        var parts = storageKey.Split('/');
        var dirPath = string.Join("/", parts[..^1]);
        var fileName = parts[^1];
        var dirClient = shareClient.GetDirectoryClient(dirPath);
        var fileClient = dirClient.GetFileClient(fileName);
        await fileClient.DeleteIfExistsAsync(cancellationToken: ct);
    }

    public async Task<IReadOnlyList<StoredFileInfo>> ListAsync(string bucketPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_opts.ConnectionString) || _opts.ConnectionString.Contains("<"))
            return Array.Empty<StoredFileInfo>();

        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient = serviceClient.GetShareClient(_opts.ShareName);
        var dirClient = shareClient.GetDirectoryClient(bucketPath.Trim('/'));

        var result = new List<StoredFileInfo>();
        await foreach (var item in dirClient.GetFilesAndDirectoriesAsync(cancellationToken: ct))
        {
            if (!item.IsDirectory)
            {
                var storageKey = (bucketPath.Trim('/') + "/" + item.Name).Trim('/');
                var fileClient = dirClient.GetFileClient(item.Name);
                var props = await fileClient.GetPropertiesAsync(ct);
                result.Add(new StoredFileInfo(
                    storageKey,
                    item.Name,
                    props.Value.ContentLength,
                    props.Value.LastModified));
            }
        }
        return result.OrderByDescending(f => f.LastModified).ToList();
    }

    public async Task<byte[]> DownloadAsync(string storageKey, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient = serviceClient.GetShareClient(_opts.ShareName);
        var parts = storageKey.Split('/');
        var dirPath = string.Join("/", parts[..^1]);
        var fileName = parts[^1];
        var dirClient = shareClient.GetDirectoryClient(dirPath);
        var fileClient = dirClient.GetFileClient(fileName);
        var download = await fileClient.DownloadAsync(cancellationToken: ct);
        using var ms = new MemoryStream();
        await download.Value.Content.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    public async Task<Stream> OpenUploadStreamAsync(string storageKey, string contentType, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient   = serviceClient.GetShareClient(_opts.ShareName);
        await shareClient.CreateIfNotExistsAsync(cancellationToken: ct);
        var parts    = storageKey.Split('/');
        var dirPath  = string.Join("/", parts[..^1]);
        var fileName = parts[^1];
        var dirClient  = shareClient.GetDirectoryClient(dirPath);
        await dirClient.CreateIfNotExistsAsync(cancellationToken: ct);
        var fileClient = dirClient.GetFileClient(fileName);
        return await fileClient.OpenWriteAsync(overwrite: true, position: 0, cancellationToken: ct);
    }

    public async Task<Stream> DownloadStreamAsync(string storageKey, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient   = serviceClient.GetShareClient(_opts.ShareName);
        var parts     = storageKey.Split('/');
        var dirPath   = string.Join("/", parts[..^1]);
        var fileName  = parts[^1];
        var dirClient  = shareClient.GetDirectoryClient(dirPath);
        var fileClient = dirClient.GetFileClient(fileName);
        var download   = await fileClient.DownloadAsync(cancellationToken: ct);
        return download.Value.Content;
    }

    public async Task<Uri> CreatePreSignedDownloadUrlAsync(string storageKey, TimeSpan ttl, string downloadFileName, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient   = serviceClient.GetShareClient(_opts.ShareName);
        var parts     = storageKey.Split('/');
        var dirPath   = string.Join("/", parts[..^1]);
        var fileName  = parts[^1];
        var fileClient = shareClient.GetDirectoryClient(dirPath).GetFileClient(fileName);

        var connParts = _opts.ConnectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!connParts.TryGetValue("AccountName", out var accountName) ||
            !connParts.TryGetValue("AccountKey", out var accountKey))
            return fileClient.Uri;

        var safeName   = Uri.EscapeDataString(downloadFileName);
        var sasBuilder = new ShareSasBuilder
        {
            ShareName        = _opts.ShareName,
            FilePath         = storageKey,
            Resource         = "f",
            ExpiresOn        = DateTimeOffset.UtcNow.Add(ttl),
            ContentDisposition = $"attachment; filename=\"{safeName}\""
        };
        sasBuilder.SetPermissions(ShareFileSasPermissions.Read);
        var credential = new StorageSharedKeyCredential(accountName, accountKey);
        var sasToken   = sasBuilder.ToSasQueryParameters(credential).ToString();
        return new Uri($"{fileClient.Uri}?{sasToken}");
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient   = serviceClient.GetShareClient(_opts.ShareName);
        var parts     = storageKey.Split('/');
        var dirPath   = string.Join("/", parts[..^1]);
        var fileName  = parts[^1];
        var fileClient = shareClient.GetDirectoryClient(dirPath).GetFileClient(fileName);
        var exists = await fileClient.ExistsAsync(cancellationToken: ct);
        return exists.Value;
    }

    private string GenerateSasUri(ShareFileClient fileClient, string storageKey)
    {
        var connParts = _opts.ConnectionString
            .Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1]);

        if (!connParts.TryGetValue("AccountName", out var accountName) ||
            !connParts.TryGetValue("AccountKey", out var accountKey))
            return fileClient.Uri.ToString();

        var sasBuilder = new ShareSasBuilder
        {
            ShareName = _opts.ShareName,
            FilePath = storageKey,
            Resource = "f",
            ExpiresOn = DateTimeOffset.UtcNow.AddYears(10)
        };
        sasBuilder.SetPermissions(ShareFileSasPermissions.Read);

        var credential = new StorageSharedKeyCredential(accountName, accountKey);
        var sasToken = sasBuilder.ToSasQueryParameters(credential).ToString();
        return $"{fileClient.Uri}?{sasToken}";
    }
}
