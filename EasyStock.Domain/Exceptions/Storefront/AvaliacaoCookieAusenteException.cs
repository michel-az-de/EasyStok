namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Cookie de autorização de avaliação ausente ou inválido.
/// Mapeado para HTTP 401 Unauthorized.
/// </summary>
public sealed class AvaliacaoCookieAusenteException()
    : Exception("Cookie de autorização de avaliação ausente ou inválido. Abra o link enviado pelo WhatsApp.");
