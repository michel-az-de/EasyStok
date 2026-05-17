using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Services;

public sealed class S3CompatibleFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;
    private AmazonS3Client? _client;

    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
    {
        // Fail-fast: valida filename (path traversal) e MIME antes de qualquer chamada S3.
        var safeFileName = UploadSecurityValidator.SanitizeFileName(request.FileName);
        UploadSecurityValidator.EnsureValidMime(request.ContentType);

        var client = GetClient();
        var key = $"{request.BucketPath.Trim('/')}/{safeFileName}".Trim('/');

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

    public async Task<Stream> OpenUploadStreamAsync(string storageKey, string contentType, CancellationToken ct = default)
    {
        // Para S3: usamos upload multipart via MemoryStream intermediário.
        // Uma implementação completa usaria TransferUtility ou UploadPartAsync;
        // para o MVP retornamos um stream que, ao ser Dispose'd, faz o upload.
        var ms = new S3UploadStream(GetClient(), _options.S3.BucketName, storageKey, contentType);
        return await Task.FromResult<Stream>(ms);
    }

    public async Task<Stream> DownloadStreamAsync(string storageKey, CancellationToken ct = default)
    {
        var client   = GetClient();
        var response = await client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = _options.S3.BucketName,
            Key        = storageKey
        }, ct);
        return response.ResponseStream;
    }

    public Task<Uri> CreatePreSignedDownloadUrlAsync(string storageKey, TimeSpan ttl, string downloadFileName, CancellationToken ct = default)
    {
        var client  = GetClient();
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.S3.BucketName,
            Key        = storageKey,
            Expires    = DateTime.UtcNow.Add(ttl),
            Verb       = HttpVerb.GET,
            ResponseHeaderOverrides =
            {
                ContentDisposition = $"attachment; filename=\"{Uri.EscapeDataString(downloadFileName)}\""
            }
        };
        var url = client.GetPreSignedURL(request);
        return Task.FromResult(new Uri(url));
    }

    public async Task<bool> ExistsAsync(string storageKey, CancellationToken ct = default)
    {
        var client = GetClient();
        try
        {
            await client.GetObjectMetadataAsync(_options.S3.BucketName, storageKey, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    // Stream helper: bufferiza em memória e faz PutObject ao fechar.
    // Para relatórios grandes, uma implementação multipart seria mais robusta (L2).
    private sealed class S3UploadStream(AmazonS3Client client, string bucket, string key, string contentType)
        : MemoryStream
    {
        private bool _uploaded;

        protected override void Dispose(bool disposing)
        {
            if (!_uploaded)
            {
                _uploaded = true;
                Position  = 0;
                client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName  = bucket,
                    Key         = key,
                    InputStream = this,
                    ContentType = contentType
                }).GetAwaiter().GetResult();
            }
            base.Dispose(disposing);
        }
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
