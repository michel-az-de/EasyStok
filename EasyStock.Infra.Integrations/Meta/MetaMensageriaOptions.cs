namespace EasyStock.Infra.Integrations.Meta;

/// <summary>
/// <c>Atendimento:MetaMensageria</c> (S35): envio do Messenger e do Instagram pela Send API da Meta.
/// O page access token é segredo de configuração (Onda 0.10), nunca de código. Com o provider
/// diferente de <c>meta</c> o envio cai no stub, que não sai para a rede.
/// </summary>
public sealed class MetaMensageriaOptions
{
    public const string Secao = "Atendimento:MetaMensageria";

    public string Provider { get; set; } = "stub";
    public string PageAccessToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://graph.facebook.com/v25.0";
}