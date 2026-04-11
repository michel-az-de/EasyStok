using Azure.Storage;
using Azure.Storage.Files.Shares;
using Azure.Storage.Sas;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Storage;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Services;

public sealed class AzureFileShareStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly AzureFileShareStorageOptions _opts = options.Value.AzureFileShare;

    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken ct = default)
    {
        var serviceClient = new ShareServiceClient(_opts.ConnectionString);
        var shareClient = serviceClient.GetShareClient(_opts.ShareName);
        await shareClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var dirPath = request.BucketPath.Trim('/');
        var dirClient = shareClient.GetDirectoryClient(dirPath);
        await dirClient.CreateIfNotExistsAsync(cancellationToken: ct);

        var fileClient = dirClient.GetFileClient(request.FileName);
        using var stream = new MemoryStream(request.Content);
        await fileClient.CreateAsync(stream.Length, cancellationToken: ct);
        await fileClient.UploadRangeAsync(new Azure.HttpRange(0, stream.Length), stream, cancellationToken: ct);

        var storageKey = $"{dirPath}/{request.FileName}".Trim('/');
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
