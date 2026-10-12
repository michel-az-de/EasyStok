namespace EasyStock.Application.Ports.Output.Atendimento;

/// <summary>
/// Chamadas à Graph API do Embedded Signup v4 com coexistência (#1417): o número continua no app WhatsApp
/// Business do celular e passa a falar também pela Cloud API. Todas, menos a troca do <c>code</c>, usam o
/// business token da empresa (nunca o token global do app). Falha da Meta vira <see cref="WhatsAppCloudException"/>.
/// </summary>
public interface IMetaEmbeddedSignupClient
{
    /// <summary><c>GET oauth/access_token</c> com o <c>client_id</c> e o <c>client_secret</c> do app: devolve o business token.</summary>
    Task<string> TrocarCodigoPorTokenAsync(string code, CancellationToken ct = default);

    /// <summary><c>POST {waba}/subscribed_apps</c>: sem isso o webhook do app não recebe nada da WABA da loja.</summary>
    Task InscreverAppNaWabaAsync(string wabaId, string token, CancellationToken ct = default);

    /// <summary><c>GET {phone_number_id}</c> com os campos que dizem se o número está mesmo no app Business.</summary>
    Task<NumeroWhatsAppMeta> ConsultarNumeroAsync(string phoneNumberId, string token, CancellationToken ct = default);

    /// <summary><c>POST {phone_number_id}/smb_app_data</c>: pede a sincronização; devolve o <c>request_id</c> da Meta.</summary>
    Task<string?> SolicitarSincronizacaoAsync(
        string phoneNumberId, string token, TipoSincronizacaoWhatsApp tipo, CancellationToken ct = default);
}

/// <summary>O que a Meta diz do número. <see cref="IsOnBizApp"/> <c>true</c> é a coexistência ativa.</summary>
public sealed record NumeroWhatsAppMeta(
    string? DisplayPhoneNumber,
    string? VerifiedName,
    bool? IsOnBizApp,
    string? PlatformType);

/// <summary>Os dois pedidos de sincronização da coexistência (#1417).</summary>
public enum TipoSincronizacaoWhatsApp
{
    /// <summary><c>smb_app_state_sync</c>: contatos do app.</summary>
    EstadoDoApp = 1,

    /// <summary><c>history</c>: até 180 dias de conversa (chega pelo webhook <c>history</c>).</summary>
    Historico = 2,
}
