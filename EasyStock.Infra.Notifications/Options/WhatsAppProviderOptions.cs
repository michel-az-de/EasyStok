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
    public string BaseUrl { get; set; } = "https://graph.facebook.com/v19.0";

    /// <summary>App Secret do app da Meta — assina o corpo do webhook (X-Hub-Signature-256).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Token arbitrário definido por nós, conferido no GET de verificação do webhook (hub.verify_token).</summary>
    public string VerifyToken { get; set; } = string.Empty;

    /// <summary>Versão da Graph API usada nas chamadas (ex.: "v19.0").</summary>
    public string ApiVersion { get; set; } = "v19.0";

    public string? WabaId { get; set; }
}
