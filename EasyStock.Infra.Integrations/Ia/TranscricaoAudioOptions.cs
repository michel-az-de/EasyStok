namespace EasyStock.Infra.Integrations.Ia;

/// <summary>
/// Seção <c>Transcricao</c> (#1398): servidor Whisper compatível com OpenAI <c>audio/transcriptions</c>.
/// Padrão: faster-whisper próprio na VPS (<c>ez-whisper</c>, ADR-0058), sem chave; a Fireworks desativou o
/// áudio em 10/06/2026. <see cref="ApiKey"/> só é enviada quando preenchida (provedor externo).
/// <see cref="Enabled"/>=false ou sem <see cref="BaseUrl"/>, o áudio segue sem texto.
/// </summary>
public sealed class TranscricaoAudioOptions
{
    public const string Secao = "Transcricao";

    public bool Enabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "http://ez-whisper:8000/";
    public string Modelo { get; set; } = "Systran/faster-whisper-small";
    public string Idioma { get; set; } = "pt";
    public int TimeoutSegundos { get; set; } = 120;
}
