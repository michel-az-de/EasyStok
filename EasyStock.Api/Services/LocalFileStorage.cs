using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Services;

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options, IWebHostEnvironment environment) : IFileStorage
{
    private readonly FileStorageOptions _options = options.Value;

    public async Task<StoredFileResult> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
    {
        // Fail-fast: valida filename (path traversal) e MIME antes de qualquer IO.
        var safeFileName = UploadSecurityValidator.SanitizeFileName(request.FileName);
        UploadSecurityValidator.EnsureValidMime(request.ContentType);

        var relativePath = request.BucketPath.Replace('\\', '/').Trim('/');
        var rootPath = Path.GetFullPath(GetRootPath());
        var targetDirectory = Path.GetFullPath(Path.Combine(rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!targetDirectory.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Caminho de upload invalido.");
        Directory.CreateDirectory(targetDirectory);

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
        var rootPath = Path.GetFullPath(GetRootPath());
        var filePath = Path.GetFullPath(Path.Combine(rootPath, safeKey));
        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Caminho de exclusao invalido.");
        if (File.Exists(filePath))
            File.Delete(filePath);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredFileInfo>> ListAsync(string bucketPath, CancellationToken cancellationToken = default)
    {
        var rootPath = Path.GetFullPath(GetRootPath());
        var dirPath = Path.GetFullPath(Path.Combine(rootPath, bucketPath.Replace('/', Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar)));
        if (!dirPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(dirPath))
            return Task.FromResult<IReadOnlyList<StoredFileInfo>>(Array.Empty<StoredFileInfo>());

        var files = new DirectoryInfo(dirPath)
            .GetFiles("*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new StoredFileInfo(
                (bucketPath.Trim('/') + "/" + f.Name).Trim('/'),
                f.Name,
                f.Length,
                new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToList();

        return Task.FromResult<IReadOnlyList<StoredFileInfo>>(files);
    }

    public Task<byte[]> DownloadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var safeKey = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var rootPath = Path.GetFullPath(GetRootPath());
        var filePath = Path.GetFullPath(Path.Combine(rootPath, safeKey));
        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Caminho invalido.");
        return File.ReadAllBytesAsync(filePath, cancellationToken);
    }

    public string GetRootPath()
    {
        if (Path.IsPathRooted(_options.LocalRootPath))
            return _options.LocalRootPath;

        return Path.Combine(environment.ContentRootPath, _options.LocalRootPath);
    }
}
