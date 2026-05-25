namespace EasyStock.Domain.Enums.Pagamentos;

/// <summary>
/// Classificacao do erro de uma chamada a gateway. Direciona a politica de
/// retry vs fallback no <c>IPagamentoOrchestrator</c>.
///
/// <para>
/// Mapeamento por categoria:
/// </para>
/// <list type="bullet">
///   <item><c>Network</c>, <c>Server5xx</c>: retry no mesmo gateway com backoff exponencial.</item>
///   <item><c>Timeout</c>: retry, mas sempre <c>ConsultarAsync</c> antes pra evitar duplo charge.</item>
///   <item><c>RateLimit</c> (429): retry respeitando header <c>Retry-After</c>.</item>
///   <item><c>Declined</c> (cartao recusado, fundos insuficientes): fallback IMEDIATO pra proximo gateway.</item>
///   <item><c>InvalidData</c> (4xx semantico de payload): NAO retry, NAO fallback — bug do caller, abre ticket.</item>
///   <item><c>GatewayDown</c> (provedor sinaliza outage): fallback proximo gateway.</item>
///   <item><c>Unknown</c>: retry conservador (max 2) e depois fallback.</item>
/// </list>
/// </summary>
public enum ErrorCategory : byte
{
    Network = 1,
    Timeout = 2,
    Server5xx = 3,
    RateLimit = 4,
    Declined = 5,
    InvalidData = 6,
    GatewayDown = 7,
    Unknown = 99
}
