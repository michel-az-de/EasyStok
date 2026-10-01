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
    /// <summary>
    /// Allowlist da mídia recebida do cliente (upload privado, nunca servido público). Mais larga que a
    /// do upload público porque o cliente manda nota de voz, vídeo e documento (issue 1285).
    /// </summary>
    public static readonly IReadOnlySet<string> MimesPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif",
        "audio/ogg", "audio/mpeg", "audio/mp4", "audio/aac", "audio/amr",
        "video/mp4", "video/3gpp",
        "application/pdf", "text/plain", "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

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
            new FileUploadRequest(bucketPath, fileName, mime, memoria.ToArray(), IsPublic: false, MimesPermitidos), ct);

        return (resultado.StorageKey, resultado.ContentType);
    }

    // A nota de voz chega como "audio/ogg; codecs=opus": a extensão sai do tipo sem parâmetros.
    private static string ExtensaoPara(string mimeType) => mimeType.Split(';', 2)[0].Trim().ToLowerInvariant() switch
    {
        "image/jpeg" => ".jpg",
        "image/png" => ".png",
        "image/webp" => ".webp",
        "image/gif" => ".gif",
        "audio/ogg" => ".ogg",
        "audio/mpeg" or "audio/mp3" => ".mp3",
        "audio/mp4" => ".m4a",
        "audio/aac" => ".aac",
        "audio/amr" => ".amr",
        "video/mp4" => ".mp4",
        "video/3gpp" => ".3gp",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        "application/msword" => ".doc",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
        _ => ""
    };
}
