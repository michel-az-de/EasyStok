namespace EasyStock.Infra.Integrations.WhatsApp.Waha;

/// <summary>
/// Ponte provisória do WhatsApp pelo WAHA (#2072, ADR-0062), seção <c>Atendimento:WhatsApp:Waha</c>. Só vale com
/// <c>Atendimento:WhatsApp:Cliente=waha</c>.
/// </summary>
public sealed class WahaOptions
{
    public const string Secao = "Atendimento:WhatsApp:Waha";

    /// <summary>
    /// Ponte ligada: preenchido no registro a partir de <c>Atendimento:WhatsApp:Cliente=waha</c>, não pela seção.
    /// Desligada, o webhook do WAHA responde 404.
    /// </summary>
    public bool Ligada { get; set; }

    /// <summary>Endereço do WAHA na rede interna (ex.: <c>http://ez-waha:3000</c>). Nunca exposto na internet.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Chave da API do WAHA, enviada em <c>X-Api-Key</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Segredo do HMAC sha512 que o WAHA põe em <c>X-Webhook-Hmac</c>.</summary>
    public string HmacKey { get; set; } = string.Empty;

    /// <summary>Sessão do WAHA (no Core só existe <c>default</c>).</summary>
    public string Sessao { get; set; } = "default";

    /// <summary>
    /// Número da loja só com dígitos (ex.: <c>5541999990000</c>). É o <c>phone_number_id</c> que a empresa vincula
    /// em Admin › Tenants › WhatsApp: o webhook roteia por ele e o envio só sai para a empresa vinculada a ele.
    /// </summary>
    public string PhoneNumberId { get; set; } = string.Empty;

    /// <summary>Pausa mínima com "digitando" antes de cada envio, em milissegundos.</summary>
    public int PausaMinimaMs { get; set; } = 1200;

    /// <summary>Teto da pausa com "digitando", em milissegundos.</summary>
    public int PausaMaximaMs { get; set; } = 4500;

    /// <summary>Quanto cada caractere do texto alonga a pausa, em milissegundos.</summary>
    public int PausaPorCaractereMs { get; set; } = 30;

    public int TimeoutSegundos { get; set; } = 30;

    public string PhoneNumberIdNormalizado => new(PhoneNumberId.Where(char.IsAsciiDigit).ToArray());
}
