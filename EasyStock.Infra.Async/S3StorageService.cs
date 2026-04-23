using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;

namespace EasyStock.Infra.Async;

/// <summary>
/// Adaptador do IFileStorage existente para IStorageService.
/// Usa apenas as interfaces do Application layer.
/// </summary>
public sealed class S3StorageService(IFileStorage fileStorage) : IStorageService
{
    /// <summary>Limite máximo em bytes por upload. Evita OOM em arquivos gigantes.</summary>
    public const long MaxUploadSizeBytes = 50 * 1024 * 1024; // 50 MB

    public async Task<string> UploadAsync(string container, string fileName, Stream content, string contentType)
    {
        // Fail-fast: valida filename (path traversal) e MIME — defense-in-depth, o IFileStorage
        // subjacente também valida, mas erros precoces evitam carregar o stream inteiro em memória.
        var safeFileName = UploadSecurityValidator.SanitizeFileName(fileName);
        UploadSecurityValidator.EnsureValidMime(contentType);

        // Validação precoce quando o stream expõe Length (a maioria dos uploads web expõe).
        if (content.CanSeek && content.Length > MaxUploadSizeBytes)
            throw new InvalidOperationException(
                $"Arquivo excede o limite de {MaxUploadSizeBytes / (1024 * 1024)}MB. Tamanho recebido: {content.Length} bytes.");

        // Para streams sem Length conhecido (ex.: chunked), copiar com limite dinâmico.
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await content.ReadAsync(buffer)) > 0)
        {
            totalRead += read;
            if (totalRead > MaxUploadSizeBytes)
                throw new InvalidOperationException(
                    $"Arquivo excede o limite de {MaxUploadSizeBytes / (1024 * 1024)}MB durante a leitura.");
            await ms.WriteAsync(buffer.AsMemory(0, read));
        }

        var request = new FileUploadRequest(
            BucketPath: container,
            FileName: safeFileName,
            ContentType: contentType,
            Content: ms.ToArray(),
            IsPublic: true);
        var result = await fileStorage.UploadAsync(request);
        return result.StorageKey;
    }

    public Task<string> UploadAsync(string container, string fileName, Stream content, string contentType, Dictionary<string, string> metadata) =>
        // Metadata nao suportado pela interface atual.
        UploadAsync(container, fileName, content, contentType);

    public Task<Stream> DownloadAsync(string container, string fileName)
    {
        // IFileStorage nao suporta download direto.
        throw new NotSupportedException("Download nao e suportado pela interface IFileStorage atual");
    }

    public Task<string> GetPublicUrlAsync(string container, string fileName, TimeSpan? expiry = null)
    {
        // Para obter URL, precisamos fazer upload primeiro ou ter o storage key.
        // Esta implementacao e limitada pela interface atual.
        throw new NotSupportedException("GetPublicUrl requer storage key, nao implementado nesta adaptacao");
    }

    public Task DeleteAsync(string container, string fileName)
    {
        // Sanitiza fileName para impedir path traversal via delete.
        var safeFileName = UploadSecurityValidator.SanitizeFileName(fileName);
        // Assumindo que o storage key e container/filename.
        return fileStorage.DeleteAsync($"{container}/{safeFileName}");
    }

    public Task<bool> ExistsAsync(string container, string fileName)
    {
        // IFileStorage nao suporta verificacao de existencia.
        throw new NotSupportedException("Verificacao de existencia nao e suportada pela interface IFileStorage atual");
    }

    public Task<IEnumerable<string>> ListFilesAsync(string container, string prefix = "")
    {
        // IFileStorage nao suporta listagem.
        throw new NotSupportedException("Listagem de arquivos nao e suportada pela interface IFileStorage atual");
    }
}
