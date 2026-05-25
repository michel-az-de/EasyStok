namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Fonte do <c>WebhookSecret</c> do MercadoPago — usado por
/// <see cref="UseCases.Storefront.Webhook.MpHmacValidator"/> e
/// <see cref="UseCases.Storefront.Webhook.ReceberWebhookMpUseCase"/>.
///
/// <para>
/// MVP: secret global da app (config <c>MercadoPago:WebhookSecret</c>) por
/// estarmos com um tenant ativo (Casa da Babá). Futuro multi-tenant: resolve
/// por <c>EmpresaId</c> via <c>CredencialIntegracao</c>.
/// </para>
///
/// <para>
/// <strong>NUNCA</strong> log o valor retornado em texto plano.
/// </para>
/// </summary>
public interface IMpWebhookSecretProvider
{
    /// <summary>
    /// Retorna o secret HMAC ativo. Lança caso não configurado — webhook não pode
    /// validar HMAC sem secret.
    /// </summary>
    string ObterSecret();
}
