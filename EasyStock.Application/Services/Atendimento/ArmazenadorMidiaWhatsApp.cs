using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Storage;

namespace EasyStock.Application.Services.Atendimento;

/// <summary>
/// Baixa mídia recebida do cliente pela Cloud API e guarda no storage privado (S3 compatível), em
/// <c>atendimento/{empresaId}/{conversaId}/{wamid}.{ext}</c>. A chave devolvida é o que
/// <c>Mensagem.MidiaChave</c> (S04) grava — servida depois por endpoint autenticado, nunca pública.
/// </summary>
public sealed class ArmazenadorMidiaWhatsApp(IWhatsAppCloudClient cloudClient, IFileStorage fileStorage)
{
    public async Task<(string Chave, string Mime)> ArmazenarAsync(
        Guid empresaId, Guid conversaId, string wamid, string mediaId, CancellationToken ct = default)
    {
        var (conteudo, mime) = await cloudClient.BaixarMidiaAsync(mediaId, ct);

        await using var _ = conteudo;
        using var memoria = new MemoryStream();
        await conteudo.CopyToAsync(memoria, ct);

        var bucketPath = $"atendimento/{empresaId}/{conversaId}";
        var fileName = $"{wamid}{ExtensaoPara(mime)}";

        var resultado = await fileStorage.UploadAsync(
            new FileUploadRequest(bucketPath, fileName, mime, memoria.ToArray(), IsPublic: false), ct);

        return (resultado.StorageKey, resultado.ContentType);
    }

    private static string ExtensaoPara(string mimeType) => mimeType switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "audio/ogg" => ".ogg",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "application/pdf" => ".pdf",
        _ => ""
    };
}
