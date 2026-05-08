namespace EasyStock.Infra.Integrations.Fiscal.FocusNFe;

/// <summary>
/// Schema do payload cifrado em <c>credencial_integracao</c> quando
/// <c>categoria=Fiscal</c> e <c>provider_key="focusnfe"</c>.
/// O resolver desserializa o JSON decifrado para este tipo.
/// </summary>
public sealed class FocusNFeCredencial
{
    public string TokenFocus { get; set; } = "";
    public string? CscId { get; set; }
    public string? Csc { get; set; }
    public string? WebhookSecret { get; set; }
}
