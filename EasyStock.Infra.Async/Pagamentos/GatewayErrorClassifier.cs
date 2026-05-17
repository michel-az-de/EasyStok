using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Infra.Async.Pagamentos;

/// <summary>
/// Classificacao basica em P0: avalia tipo de excecao e statusCode HTTP
/// (quando disponivel) para mapear em <see cref="ErrorCategory"/>.
///
/// <para>
/// Em P2 (Stripe.net + MercadoPago HTTP), expande matching de codigos
/// especificos por gateway (ex: Stripe <c>card_declined</c> → <c>Declined</c>).
/// </para>
/// </summary>
public sealed class GatewayErrorClassifier : IGatewayErrorClassifier
{
    public ErrorCategory Classify(string provedor, Exception ex, int? statusCode = null, string? gatewayCode = null)
    {
        ArgumentNullException.ThrowIfNull(ex);

        // Tipo de excecao primeiro (precedencia mais alta).
        switch (ex)
        {
            case TaskCanceledException:
            case TimeoutException:
                return ErrorCategory.Timeout;
            case HttpRequestException httpEx:
                // Quando o HttpRequestException carrega StatusCode, usa-o.
                if (httpEx.StatusCode is HttpStatusCode sc)
                    statusCode ??= (int)sc;
                if (statusCode is null)
                    return ErrorCategory.Network;
                break;
            case SocketException:
            case System.IO.IOException:
                return ErrorCategory.Network;
            case NotImplementedException:
            case NotSupportedException:
            case ArgumentException:
                return ErrorCategory.InvalidData;
        }

        if (statusCode is int code)
        {
            return code switch
            {
                429 => ErrorCategory.RateLimit,
                >= 500 and <= 599 => ErrorCategory.Server5xx,
                400 or 422 => ErrorCategory.InvalidData,
                401 or 403 => ErrorCategory.GatewayDown, // credenciais invalidas/bloqueadas
                402 => ErrorCategory.Declined, // Payment Required
                404 => ErrorCategory.InvalidData,
                _ => ErrorCategory.Unknown
            };
        }

        // P2: matching por (provedor, gatewayCode) entra aqui.
        return ErrorCategory.Unknown;
    }
}
