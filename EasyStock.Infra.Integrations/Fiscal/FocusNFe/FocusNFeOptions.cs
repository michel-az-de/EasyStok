namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Configuracao do adapter Focus NFe. Bind no appsettings.json em <c>FocusNFe</c>.
///
/// <para>
/// Token e CSC sao por tenant — vivem em <c>CredencialIntegracao</c> cifrada,
/// nao aqui. Esta classe e apenas para URL/timeout/comportamento global.
/// </para>
/// </summary>
public sealed class FocusNFeOptions
{
    public const string SectionName = "FocusNFe";

    /// <summary>URL base. Sandbox: <c>https://homologacao.focusnfe.com.br/v2/</c>. Producao: <c>https://api.focusnfe.com.br/v2/</c>.</summary>
    public string BaseUrl { get; set; } = "https://homologacao.focusnfe.com.br/v2/";

    /// <summary>Timeout total da chamada HTTP (default 30s).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Secret HMAC para validar assinatura do webhook (header <c>X-Focus-Signature</c>).
    /// Configurado uma vez no painel Focus por ambiente. Sandbox + Producao podem ter secrets distintos.
    /// </summary>
    public string? WebhookSecret { get; set; }

    /// <summary>Quando <c>true</c>, o adapter loga payload completo (apenas em desenvolvimento — pode vazar dados fiscais).</summary>
    public bool LogPayloadCompleto { get; set; }
}
