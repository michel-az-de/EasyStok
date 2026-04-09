using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Storage;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Services;

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options, IWebHostEnvironment environment) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
    {
        var relativePath = request.BucketPath.Replace('\\', '/').Trim('/');
        var rootPath = GetRootPath();
        var targetDirectory = Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(targetDirectory);

        var safeFileName = Path.GetFileName(request.FileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            throw new ArgumentException("O nome do arquivo nao pode ser vazio.", nameof(request));

        var filePath = Path.Combine(targetDirectory, safeFileName);
        await File.WriteAllBytesAsync(filePath, request.Content, cancellationToken);

        var storageKey = $"{relativePath}/{safeFileName}".Trim('/');
        var publicBaseUrl = _options.PublicBaseUrl.TrimEnd('/');
        var url = $"{publicBaseUrl}/{storageKey}".Replace("\\", "/");

        return new StoredFileResult(storageKey, url, request.ContentType, request.Content.LongLength);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var safeKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(GetRootPath(), safeKey);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public string GetRootPath()
    {
        if (Path.IsPathRooted(_options.LocalRootPath))
            return _options.LocalRootPath;

        return Path.Combine(environment.ContentRootPath, _options.LocalRootPath);
    }
}
