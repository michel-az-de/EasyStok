namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

public sealed class MercadoPagoOptions
{
    public const string Section = "MercadoPago";

    public string AccessToken { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.mercadopago.com/";
    public string? NotificationUrl { get; set; }
    public string? BackUrlSuccess { get; set; }
    public string? BackUrlFailure { get; set; }
    public string? BackUrlPending { get; set; }
}
