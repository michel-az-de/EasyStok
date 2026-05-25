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
}
