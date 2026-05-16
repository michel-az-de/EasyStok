namespace EasyStock.Infra.Notifications.Options;

/// <summary>
/// Onda 2.2 — config VAPID para Web Push.
/// <para>
/// VAPID (Voluntary Application Server Identification, RFC 8292) eh par de chaves
/// ECDH P-256 que prova ao push service que o backend eh autorizado a enviar
/// para uma subscription. Subject eh um mailto: ou URL de contato do dev (push
/// service usa para entrar em contato se a app abusar — nao eh validado).
/// </para>
/// <para>
/// Geracao: <c>VapidHelper.GenerateVapidKeys()</c> (uma vez, em prod via fly secrets).
/// </para>
/// </summary>
public sealed class WebPushOptions
{
    /// <summary>"mailto:admin@easystok.com" ou URL https.</summary>
    public string Subject { get; set; } = "mailto:contato@easystok.com";

    /// <summary>Chave publica VAPID em base64url. Frontend usa em PushManager.subscribe.</summary>
    public string PublicKey { get; set; } = "";

    /// <summary>Chave privada VAPID em base64url. NUNCA exponha — assinatura JWT.</summary>
    public string PrivateKey { get; set; } = "";
}
