using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Classifica excecao de chamada a gateway em <see cref="ErrorCategory"/>.
/// Mapeia codigos especificos do Stripe / MercadoPago / Efí para categorias
/// genericas que direcionam a politica de retry/fallback.
///
/// <para>
/// Implementacao em P0: classificacao basica (tipo de excecao, statusCode HTTP
/// quando disponivel). Em P2 com SDKs reais, expande matching de codigos por
/// gateway (ex: Stripe <c>card_declined</c> → <c>Declined</c>).
/// </para>
/// </summary>
public interface IGatewayErrorClassifier
{
    ErrorCategory Classify(string provedor, Exception ex, int? statusCode = null, string? gatewayCode = null);
}
