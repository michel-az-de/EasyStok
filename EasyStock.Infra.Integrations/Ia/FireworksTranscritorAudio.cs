using System.Net.Http.Headers;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Ai;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Ia;

/// <summary>
/// #1398: <c>POST v1/audio/transcriptions</c> (multipart <c>file</c>, <c>model</c>, <c>language</c>) com
/// <c>Authorization: Bearer</c>. Resposta <c>{ "text": "..." }</c>. A chave nunca vai para o log.
/// </summary>
public sealed class FireworksTranscritorAudio(
    HttpClient http,
    IOptions<TranscricaoAudioOptions> options,
    ILogger<FireworksTranscritorAudio> logger) : ITranscritorAudio
{
    private const string Endpoint = "v1/audio/transcriptions";
    private readonly TranscricaoAudioOptions _opcoes = options.Value;

    public bool Disponivel => _opcoes.Enabled && !string.IsNullOrWhiteSpace(_opcoes.ApiKey);

    public async Task<string?> TranscreverAsync(byte[] audio, string mime, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        if (!Disponivel)
            throw new InvalidOperationException("Transcrição de áudio desligada (Transcricao:Enabled/ApiKey).");

        using var arquivo = new ByteArrayContent(audio);
        arquivo.Headers.ContentType = MediaTypeHeaderValue.TryParse(mime, out var tipo)
            ? tipo : new MediaTypeHeaderValue("application/octet-stream");

        using var form = new MultipartFormDataContent
        {
            { arquivo, "file", "audio" + Extensao(mime) },
            { new StringContent(_opcoes.Modelo), "model" },
            { new StringContent(_opcoes.Idioma), "language" },
            { new StringContent("json"), "response_format" }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opcoes.ApiKey);

        using var response = await http.SendAsync(request, ct);
        var corpo = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Fireworks retornou {StatusCode} na transcrição: {Erro}",
                (int)response.StatusCode, corpo.Length > 500 ? corpo[..500] : corpo);
            throw new HttpRequestException($"Fireworks respondeu {(int)response.StatusCode}.", null, response.StatusCode);
        }

        using var doc = JsonDocument.Parse(corpo);
        var texto = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() : null;
        return string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
    }

    private static string Extensao(string mime) => mime.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "audio/ogg" => ".ogg",
        "audio/mpeg" => ".mp3",
        "audio/mp4" or "audio/aac" => ".m4a",
        "audio/amr" => ".amr",
        "audio/wav" or "audio/x-wav" => ".wav",
        _ => ".ogg"
    };
}
