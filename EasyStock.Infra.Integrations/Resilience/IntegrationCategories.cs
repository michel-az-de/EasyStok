namespace EasyStock.Infra.Integrations.Resilience;

/// <summary>
/// Identificadores das categorias de integração externa. Servem de chave
/// pros pipelines Polly registrados via
/// <see cref="DependencyInjection.IntegrationsServiceCollectionExtensions.AddEasyStockIntegrationResilience"/>
/// e pros resolvers de provider keyed-DI.
///
/// <para>
/// Mantenha como string constants (não enum) — pipelines do
/// <c>Microsoft.Extensions.Resilience</c> são chaveados por string.
/// </para>
/// </summary>
public static class IntegrationCategories
{
    /// <summary>
    /// Leitura do Mercado Pago (GET): repete falha de rede e resposta 5xx/408/429 (#2177). Fora de <see cref="All"/>,
    /// que só repete exceção.
    /// </summary>
    public const string Payments = "payments";

    /// <summary>
    /// Escrita do Mercado Pago (preferência, estorno, expiração): disjuntor e timeout, sem retry. A cobrança não repete
    /// sozinha (invariante 12, #2177), nem com <c>X-Idempotency-Key</c>.
    /// </summary>
    public const string PagamentosEscrita = "pagamentos-escrita";

    /// <summary>Google Routes (<c>computeRoutes</c>, POST de leitura): 1 retry curto e disjuntor próprio (#2177).</summary>
    public const string GoogleRotas = "google-rotas";

    /// <summary>TomTom, rota e geocoding: o mesmo pipeline do <see cref="GoogleRotas"/>, com disjuntor próprio (#2177).</summary>
    public const string TomTom = "tomtom";

    /// <summary>
    /// Agente na API Messages da Anthropic: só disjuntor (#2177). O POST não tem chave de idempotência e o timeout é o
    /// do <c>HttpClient</c>, que a configuração ajusta.
    /// </summary>
    public const string Anthropic = "anthropic";

    public const string Logistics = "logistics";
    public const string WhatsApp = "whatsapp";

    /// <summary>
    /// Envio de mensagem pela Cloud API (<c>POST /messages</c>): circuit breaker e timeout, sem retry,
    /// porque o POST não é idempotente (#1292). Fora de <see cref="All"/>, que recebe o pipeline com retry.
    /// </summary>
    public const string WhatsAppEnvio = "whatsapp-envio";

    /// <summary>
    /// Envio de Instagram e Messenger (<c>POST me/messages</c>): o mesmo pipeline sem retry do
    /// <see cref="WhatsAppEnvio"/>, com disjuntor próprio, para a falha de um canal não fechar o outro (#2025).
    /// </summary>
    public const string MetaEnvio = "meta-envio";

    /// <summary>
    /// Envio pela ponte do WAHA (#2072): o mesmo pipeline sem retry, com disjuntor próprio, para o WAHA fora do ar não
    /// fechar o número de plataforma, que segue na Meta.
    /// </summary>
    public const string WahaEnvio = "waha-envio";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Logistics, WhatsApp,
    };
}
