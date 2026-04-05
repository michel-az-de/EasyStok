using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Storage;

namespace EasyStock.Infra.Async;

/// <summary>
/// Adaptador do IFileStorage existente para IStorageService.
/// Usa apenas as interfaces do Application layer.
/// </summary>
public sealed class S3StorageService(IFileStorage fileStorage) : IStorageService
{
    public async Task<string> UploadAsync(string container, string fileName, Stream content, string contentType)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        var request = new FileUploadRequest(
            BucketPath: container,
            FileName: fileName,
            ContentType: contentType,
            Content: ms.ToArray(),
            IsPublic: true);

        var result = await fileStorage.UploadAsync(request);
        return result.StorageKey;
    }

    public async Task<string> UploadAsync(string container, string fileName, Stream content, string contentType, Dictionary<string, string> metadata)
    {
        // Metadata não suportado pela interface atual
        return await UploadAsync(container, fileName, content, contentType);
    }

    public Task<Stream> DownloadAsync(string container, string fileName)
    {
        // IFileStorage não suporta download direto
        throw new NotSupportedException("Download não é suportado pela interface IFileStorage atual");
    }

    public async Task<string> GetPublicUrlAsync(string container, string fileName, TimeSpan? expiry = null)
    {
        // Para obter URL, precisamos fazer upload primeiro ou ter o storage key
        // Esta implementação é limitada pela interface atual
        throw new NotSupportedException("GetPublicUrl requer storage key, não implementado nesta adaptação");
    }

    public async Task DeleteAsync(string container, string fileName)
    {
        // Assumindo que o storage key é container/filename
        var storageKey = $"{container}/{fileName}";
        await fileStorage.DeleteAsync(storageKey);
    }

    public Task<bool> ExistsAsync(string container, string fileName)
    {
        // IFileStorage não suporta verificação de existência
        return Task.FromResult(false);
    }

    public Task<IEnumerable<string>> ListFilesAsync(string container, string prefix = "")
    {
        // IFileStorage não suporta listagem
        return Task.FromResult(Enumerable.Empty<string>());
    }
}