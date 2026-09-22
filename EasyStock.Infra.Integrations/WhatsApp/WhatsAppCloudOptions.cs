namespace EasyStock.Infra.Integrations.WhatsApp;

/// <summary>
/// Subconjunto de <c>Notifications:WhatsApp:Meta</c> que o <see cref="WhatsAppCloudClient"/>
/// precisa. Options própria (em vez de referenciar EasyStock.Infra.Notifications) para não criar
/// dependência entre dois módulos de infra irmãos — ambos só dependem de Domain/Application.
/// </summary>
public sealed class WhatsAppCloudOptions
{
    public string AccessToken { get; set; } = string.Empty;
    public string PhoneNumberId { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://graph.facebook.com/v19.0";
}
