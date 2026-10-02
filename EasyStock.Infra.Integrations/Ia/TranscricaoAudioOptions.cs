namespace EasyStock.Infra.Integrations.Ia;

/// <summary>
/// Seção <c>Transcricao</c> (#1398): Whisper da Fireworks, compatível com OpenAI
/// <c>audio/transcriptions</c>. Sem <see cref="ApiKey"/>, reaproveita <c>Anthropic:ApiKeyAgente</c>
/// quando o agente já fala com a Fireworks (<c>Anthropic:AutenticacaoBearer=true</c>). Sem chave ou
/// <see cref="Enabled"/>=false, o transcritor fica indisponível e o áudio segue sem texto.
/// </summary>
public sealed class TranscricaoAudioOptions
{
    public const string Secao = "Transcricao";

    public bool Enabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public string BaseUrl { get; set; } = "https://audio-prod.api.fireworks.ai/";
    public string Modelo { get; set; } = "whisper-v3-turbo";
    public string Idioma { get; set; } = "pt";
    public int TimeoutSegundos { get; set; } = 60;
}
