namespace EasyStock.Infra.Notifications.Options;

public sealed class TwilioSmsOptions
{
    public string AccountSid { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
}

public sealed class ZenviaSmsOptions
{
    public string ApiToken { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.zenvia.com/v2";
}
