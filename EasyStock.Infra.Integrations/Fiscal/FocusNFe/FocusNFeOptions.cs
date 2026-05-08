namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Configuração do adapter Focus NFe lida via <c>IOptions&lt;FocusNFeOptions&gt;</c>
/// vinculada à seção "FocusNFe" em appsettings.
/// </summary>
public sealed class FocusNFeOptions
{
    public string BaseUrl { get; set; } = "https://api.focusnfe.com.br";
    public string SandboxUrl { get; set; } = "https://homologacao.focusnfe.com.br";
    public TimeSpan EmissaoTimeout { get; set; } = TimeSpan.FromSeconds(8);
    public TimeSpan ConsultaTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public TimeSpan CancelamentoTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>
    /// Secret HMAC compartilhado com Focus para validar webhooks. NUNCA
    /// commitado em texto puro — vem de Vault / KeyVault em produção.
    /// </summary>
    public string WebhookSecret { get; set; } = "";

    public int MaxRetries { get; set; } = 1;
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Resolve a base URL conforme o ambiente fiscal — produção real ou
    /// homologação. Testes E2E usam sandbox.
    /// </summary>
    public string ResolverBaseUrl(EasyStock.Domain.Enums.Fiscal.AmbienteSefaz ambiente) =>
        ambiente == EasyStock.Domain.Enums.Fiscal.AmbienteSefaz.Producao ? BaseUrl : SandboxUrl;
}
