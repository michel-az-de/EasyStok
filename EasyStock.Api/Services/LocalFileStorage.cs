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

    // ── Métodos de streaming para o motor de relatórios (PR-C0) ───────────────

    /// <summary>
    /// Abre FileStream para escrita streaming do artefato.
    /// O artefato é renomeado de ".tmp" para o nome final somente no Flush/Close,
    /// garantindo que leituras parciais não sejam servidas.
    /// </summary>
    public Task<Stream> OpenUploadStreamAsync(string storageKey, string contentType, CancellationToken ct)
    {
        var safeKey  = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var rootPath = Path.GetFullPath(GetRootPath());
        var filePath = Path.GetFullPath(Path.Combine(rootPath, safeKey));
        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Caminho de upload inválido.", nameof(storageKey));

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        // Escreve em arquivo .tmp; renomeia atomicamente ao fechar (LocalReportUploadStream).
        var tmpPath = filePath + ".tmp";
        Stream stream = new LocalReportUploadStream(tmpPath, filePath);
        return Task.FromResult(stream);
    }

    public Task<Stream> DownloadStreamAsync(string storageKey, CancellationToken ct)
    {
        var safeKey  = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var rootPath = Path.GetFullPath(GetRootPath());
        var filePath = Path.GetFullPath(Path.Combine(rootPath, safeKey));
        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Caminho inválido.", nameof(storageKey));
        if (!File.Exists(filePath))
            throw new FileNotFoundException("Artefato não encontrado.", filePath);

        // FileShare.Delete permite que o GC delete o arquivo sem erro se alguém estiver lendo.
        Stream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
                                       FileShare.Read | FileShare.Delete, bufferSize: 81_920,
                                       useAsync: true);
        return Task.FromResult(stream);
    }

    /// <summary>
    /// Gera um token JWT local com curto TTL para servir o download.
    /// Endpoint <c>GET /api/files/local-signed/{token}</c> valida e serve.
    /// </summary>
    public Task<Uri> CreatePreSignedDownloadUrlAsync(
        string storageKey, TimeSpan ttl, string downloadFileName, CancellationToken ct)
    {
        // Implementação MVP: retorna URL relativa que o endpoint de download reconhece.
        // Em produção com S3/Azure, esta implementação é substituída pela real.
        var encoded    = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(storageKey))
                         .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        var nameEnc    = Uri.EscapeDataString(downloadFileName);
        var expUnix    = DateTimeOffset.UtcNow.Add(ttl).ToUnixTimeSeconds();
        var token      = $"{encoded}.{expUnix}.{nameEnc}";
        var url        = new Uri($"/api/files/local-signed/{token}", UriKind.Relative);
        return Task.FromResult(url);
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken ct)
    {
        var safeKey  = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var rootPath = Path.GetFullPath(GetRootPath());
        var filePath = Path.GetFullPath(Path.Combine(rootPath, safeKey));
        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(false);
        return Task.FromResult(File.Exists(filePath));
    }

    public string GetRootPath()
    {
        if (Path.IsPathRooted(_options.LocalRootPath))
            return _options.LocalRootPath;

        return Path.Combine(environment.ContentRootPath, _options.LocalRootPath);
    }

    // ── Inner stream: renomeia .tmp → final ao fechar ──────────────────────────

    private sealed class LocalReportUploadStream(string tmpPath, string finalPath) : FileStream(
        tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 81_920, useAsync: true)
    {
        private bool _committed;

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Commit();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            Commit();
        }

        private void Commit()
        {
            if (_committed) return;
            _committed = true;
            if (File.Exists(tmpPath))
                File.Move(tmpPath, finalPath, overwrite: true);
        }
    }
}
