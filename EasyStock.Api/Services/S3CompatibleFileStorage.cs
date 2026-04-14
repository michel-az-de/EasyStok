using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Storage;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Services;

public sealed class S3CompatibleFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;
    private AmazonS3Client? _client;

    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var key = $"{request.BucketPath.Trim('/')}/{request.FileName}".Trim('/');

        await using var memoryStream = new MemoryStream(request.Content);
        var putRequest = new PutObjectRequest
        {
            BucketName = _options.S3.BucketName,
            Key = key,
            InputStream = memoryStream,
            ContentType = request.ContentType,
            AutoCloseStream = false
        };

        if (request.IsPublic)
            putRequest.CannedACL = S3CannedACL.PublicRead;

        await client.PutObjectAsync(putRequest, cancellationToken);

        var publicBaseUrl = string.IsNullOrWhiteSpace(_options.S3.PublicBaseUrl)
            ? BuildDefaultPublicBaseUrl()
            : _options.S3.PublicBaseUrl!.TrimEnd('/');

        return new StoredFileResult(key, $"{publicBaseUrl}/{key}", request.ContentType, request.Content.LongLength);
    }

    public async Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = _options.S3.BucketName,
            Key = storageKey
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<StoredFileInfo>> ListAsync(string bucketPath, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var prefix = bucketPath.Trim('/') + "/";
        var request = new ListObjectsV2Request
        {
            BucketName = _options.S3.BucketName,
            Prefix = prefix
        };
        var response = await client.ListObjectsV2Async(request, cancellationToken);
        return response.S3Objects
            .Where(o => !o.Key.EndsWith('/'))
            .OrderByDescending(o => o.LastModified)
            .Select(o => new StoredFileInfo(
                o.Key,
                o.Key.Split('/').Last(),
                o.Size,
                new DateTimeOffset(o.LastModified, TimeSpan.Zero)))
            .ToList();
    }

    public async Task<byte[]> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var client = GetClient();
        var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _options.S3.BucketName,
            Key = storageKey
        }, cancellationToken);
        using var ms = new MemoryStream();
        await response.ResponseStream.CopyToAsync(ms, cancellationToken);
        return ms.ToArray();
    }

    private AmazonS3Client GetClient()
    {
        if (_client is not null)
            return _client;

        var credentials = new BasicAWSCredentials(_options.S3.AccessKey, _options.S3.SecretKey);
        var config = new AmazonS3Config
        {
            ForcePathStyle = _options.S3.ForcePathStyle
        };

        if (!string.IsNullOrWhiteSpace(_options.S3.ServiceUrl))
            config.ServiceURL = _options.S3.ServiceUrl;
        else
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.S3.Region);

        _client = new AmazonS3Client(credentials, config);
        return _client;
    }

    private string BuildDefaultPublicBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.S3.ServiceUrl))
            return $"{_options.S3.ServiceUrl!.TrimEnd('/')}/{_options.S3.BucketName}";

        return $"https://{_options.S3.BucketName}.s3.{_options.S3.Region}.amazonaws.com";
    }
}
