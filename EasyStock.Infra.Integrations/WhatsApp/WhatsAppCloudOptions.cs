using EasyStock.Application.Ports.Output.Notifications;

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

    /// <summary>
    /// <c>phone_number_id</c> do número de plataforma (N6), de <c>Notifications:WhatsApp:Plataforma:PhoneNumberId</c>.
    /// Só o envio de plataforma o usa; o da loja continua resolvendo o número da empresa.
    /// </summary>
    public string PhoneNumberIdPlataforma { get; set; } = string.Empty;

    /// <summary>Id do app da Meta (#1417): <c>client_id</c> da troca do <c>code</c> do Embedded Signup.</summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>Segredo do app (o mesmo que assina o webhook): <c>client_secret</c> da troca do <c>code</c> (#1417).</summary>
    public string AppSecret { get; set; } = string.Empty;

    /// <summary>Versão da Graph API (N6): um ponto só, <see cref="VersaoGraphApi.Padrao"/>.</summary>
    public string ApiVersion { get; set; } = VersaoGraphApi.Padrao;

    /// <summary>Override da URL base, só para a homologação (fake da Meta). Vazio: deriva de <see cref="ApiVersion"/>.</summary>
    public string? BaseUrl { get; set; }

    public string BaseUrlEfetiva => string.IsNullOrWhiteSpace(BaseUrl) ? VersaoGraphApi.BaseUrlDe(ApiVersion) : BaseUrl;
}
