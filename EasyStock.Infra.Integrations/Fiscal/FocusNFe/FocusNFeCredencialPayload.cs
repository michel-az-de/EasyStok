namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Schema do payload cifrado de uma credencial Focus NFe. É o que o
/// IIntegrationCredentialResolver desserializa do payload_cifrado da
/// tabela credencial_integracao quando categoria=Fiscal e
/// provider_key="focusnfe".
/// </summary>
public sealed record FocusNFeCredencialPayload
{
    public string Token { get; init; } = "";
    public string? CscId { get; init; }
    public string? Csc { get; init; }
    public string? WebhookSecret { get; init; }
}
