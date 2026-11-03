using EasyStock.Application.Ports.Output.Atendimento;
using Microsoft.Extensions.Logging;

namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// #2155: baixa a foto de perfil que a ponte (WAHA, WPPConnect) aponta no CDN do WhatsApp. HttpClient próprio, sem a
/// chave da ponte: o endereço vem de fora e só é seguido em HTTPS para <c>*.whatsapp.net</c>, com teto de tamanho.
/// </summary>
public sealed class BaixadorFotoPerfilWhatsApp(HttpClient http, ILogger<BaixadorFotoPerfilWhatsApp> logger)
{
    public const string HttpClientNome = "whatsapp-foto-perfil";

    public async Task<FotoPerfilWhatsApp?> BaixarAsync(string? url, CancellationToken ct)
    {
        if (!EnderecoPermitido(url, out var endereco)) return null;

        using var resposta = await http.GetAsync(endereco, HttpCompletionOption.ResponseHeadersRead, ct);
        var mime = resposta.Content.Headers.ContentType?.MediaType;
        if (!resposta.IsSuccessStatusCode || mime is null || !mime.StartsWith("image/", StringComparison.Ordinal))
        {
            logger.LogWarning("Foto do perfil do WhatsApp: o CDN respondeu HTTP {Status} ({Tipo}).", (int)resposta.StatusCode, mime);
            return null;
        }
        if (resposta.Content.Headers.ContentLength > FotoPerfilWhatsApp.TetoBytes) return null;

        await resposta.Content.LoadIntoBufferAsync(FotoPerfilWhatsApp.TetoBytes, ct);
        var conteudo = await resposta.Content.ReadAsByteArrayAsync(ct);
        return conteudo.Length == 0 ? null : new FotoPerfilWhatsApp(conteudo, mime);
    }

    public static bool EnderecoPermitido(string? url, out Uri endereco)
    {
        endereco = null!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var lido) || lido.Scheme != Uri.UriSchemeHttps) return false;
        if (!lido.Host.EndsWith(".whatsapp.net", StringComparison.OrdinalIgnoreCase)) return false;
        endereco = lido;
        return true;
    }
}
