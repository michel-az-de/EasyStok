namespace EasyStock.Infra.Integrations.Ia;

/// <summary>
/// Seção <c>Anthropic</c> (a mesma do gerador de descrições). <see cref="Enabled"/> e
/// <see cref="ApiKey"/> são compartilhados; <see cref="AgenteAtendimentoEnabled"/> desliga só o agente
/// de atendimento sem afetar o resto. Chave nunca em código, teste ou log.
/// </summary>
public sealed class AnthropicAgenteOptions
{
    public const string Secao = "Anthropic";

    public bool Enabled { get; set; }
    public bool AgenteAtendimentoEnabled { get; set; } = true;
    public string? ApiKey { get; set; }
    public string ModeloAgente { get; set; } = "claude-sonnet-5";
    public int MaxTokensAgente { get; set; } = 4096;
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";
    public int TimeoutSegundos { get; set; } = 60;
}
