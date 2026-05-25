using EasyStock.Application.Ports.Output.Pagamentos;
using Microsoft.Extensions.Options;

namespace EasyStock.Infra.Integrations.Pagamentos.MercadoPago;

/// <summary>
/// Adapter MVP que resolve o <c>WebhookSecret</c> via config
/// (<see cref="MercadoPagoOptions.WebhookSecret"/>). Tenant único (Casa da Babá).
///
/// <para>
/// Evolução: substituir por implementação que resolve via
/// <c>CredencialIntegracao</c> por <c>EmpresaId</c> quando entrarmos em multi-tenant.
/// </para>
/// </summary>
public sealed class MercadoPagoWebhookSecretProvider(
    IOptions<MercadoPagoOptions> options) : IMpWebhookSecretProvider
{
    public string ObterSecret()
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrWhiteSpace(secret))
            throw new InvalidOperationException(
                "MercadoPago:WebhookSecret não configurado. Configure em appsettings ou variável de ambiente.");
        return secret;
    }
}
