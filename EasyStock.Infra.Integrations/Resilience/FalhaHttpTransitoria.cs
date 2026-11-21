using System.Net;
using Polly;
using Polly.Timeout;

namespace EasyStock.Infra.Integrations.Resilience;

/// <summary>
/// O que conta como falha passageira de HTTP nos pipelines de pagamento, rota e IA (#2177). O padrão do Polly só olha
/// exceção; o <c>HttpClient</c> devolve 503 como resposta, e sem isto o retry e o disjuntor nunca o veem.
/// </summary>
internal static class FalhaHttpTransitoria
{
    /// <summary>Repete: falha de rede ou resposta 5xx, 408, 429. Timeout não repete (estouraria o orçamento de quem chamou).</summary>
    public static bool Repete(Outcome<object> resultado) => resultado switch
    {
        { Exception: HttpRequestException } => true,
        { Result: HttpResponseMessage resposta } => Transitoria(resposta.StatusCode),
        _ => false,
    };

    /// <summary>Abre o disjuntor: o que repete e também timeout (do Polly ou do <c>HttpClient</c>). Cancelamento de quem chamou não conta.</summary>
    public static bool ContaNoDisjuntor(Outcome<object> resultado) => resultado switch
    {
        { Exception: TimeoutRejectedException } => true,
        { Exception: OperationCanceledException { InnerException: TimeoutException } } => true,
        _ => Repete(resultado),
    };

    private static bool Transitoria(HttpStatusCode status) =>
        (int)status >= 500 || status is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests;
}
