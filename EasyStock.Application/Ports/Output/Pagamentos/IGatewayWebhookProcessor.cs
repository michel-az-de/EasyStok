namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Processa o payload de um webhook ja autenticado de um gateway de pagamento.
/// Cada provedor tem seu proprio formato — implementacoes vivem em
/// <c>EasyStock.Infra.Async/Pagamentos/Webhooks/</c>.
///
/// <para>
/// <b>Idempotencia:</b> o controller generico <c>WebhookGatewayController</c>
/// registra cada evento em <see cref="Domain.Entities.WebhookRecebido"/> com
/// UNIQUE(<c>Provedor</c>, <c>EventIdExterno</c>). Se o mesmo evento chega 2x,
/// o segundo INSERT falha por chave duplicada e o processor nao e chamado
/// — fluxo retorna 200 sem reprocessar.
/// </para>
/// </summary>
public interface IGatewayWebhookProcessor
{
    /// <summary>Identificador do provedor — bate com <see cref="IPagamentoGateway.Provedor"/>.</summary>
    string Provedor { get; }

    /// <summary>
    /// Processa o payload bruto (JSON). Implementacao parseia conforme o
    /// formato do provedor e da baixa no que reconhece. Excecoes nao-fatal
    /// devem ser logadas internamente; excecoes propagadas viram 500 no
    /// controller (e o gateway vai tentar reentregar). Evento que o sistema
    /// nao reconhece volta como <see cref="ResultadoWebhookGateway.Sucesso"/>
    /// <c>false</c>: o controller registra o erro e responde 200.
    /// </summary>
    Task<ResultadoWebhookGateway> ProcessarAsync(string rawBody, IDictionary<string, string?> headers, CancellationToken ct = default);
}

/// <summary>Resultado do processamento de um webhook de gateway.</summary>
public sealed record ResultadoWebhookGateway(bool Sucesso, string? Erro = null)
{
    public static ResultadoWebhookGateway Ok { get; } = new(true);
    public static ResultadoWebhookGateway Falha(string erro) => new(false, erro);
}

/// <summary>
/// Validador de assinatura HMAC/JWT/etc do webhook. Cada provedor assina de
/// forma diferente — Efi usa HMAC SHA-256 com timestamp; Stripe usa HMAC
/// SHA-256 com schema versionado; etc.
/// </summary>
public interface IWebhookSignatureValidator
{
    /// <summary>Identificador do provedor — bate com <see cref="IGatewayWebhookProcessor.Provedor"/>.</summary>
    string Provedor { get; }

    /// <summary>
    /// Valida a assinatura do webhook. Retorna true se valida; false se
    /// invalida ou ausente (caller deve retornar 401).
    /// </summary>
    bool Validar(string rawBody, IDictionary<string, string?> headers);
}
