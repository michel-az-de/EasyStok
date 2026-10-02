using EasyStock.Application.Ports.Output.Notifications;

namespace EasyStock.Infra.Notifications.Options;

public sealed class TwilioWhatsAppOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty; // whatsapp:+14155238886
}

public sealed class MetaCloudWhatsAppOptions
{
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>Override da URL base, só para a homologação. Vazio: deriva de <see cref="ApiVersion"/> (N6).</summary>
    public string? BaseUrl { get; set; }

    public string BaseUrlEfetiva => string.IsNullOrWhiteSpace(BaseUrl) ? VersaoGraphApi.BaseUrlDe(ApiVersion) : BaseUrl;

    /// <summary>App Secret do app da Meta — assina o corpo do webhook (X-Hub-Signature-256).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Token arbitrário definido por nós, conferido no GET de verificação do webhook (hub.verify_token).</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>Versão da Graph API usada nas chamadas (N6: um ponto só, <see cref="VersaoGraphApi.Padrao"/>).</summary>
    public string ApiVersion { get; set; } = VersaoGraphApi.Padrao;

    public string? WabaId { get; set; }
}
