namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Payload cifrado em <c>credencial_integracao</c> (provider <see cref="ProviderKey"/>) com o business token que o
/// Embedded Signup devolveu para a empresa (#1417). Quem lê é o envio do atendimento; sem ela vale o token global.
/// </summary>
public sealed record CredencialWhatsAppMeta(string AccessToken, string WabaId, string PhoneNumberId, DateTime ConectadoEm)
{
    public const string ProviderKey = "meta-whatsapp";
}
