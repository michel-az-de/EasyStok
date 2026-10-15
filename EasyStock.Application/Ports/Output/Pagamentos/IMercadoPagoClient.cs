namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Porta de saída para o gateway MercadoPago (ADR-0005).
/// Implementações: <c>MercadoPagoClient</c> (HTTP direto, sem SDK estático)
/// e <c>StubMercadoPagoClient</c> (ambiente Development).
/// </summary>
public interface IMercadoPagoClient
{
    /// <summary>
    /// Cria uma Preference MP (<c>POST checkout/preferences</c>) e retorna a URL de checkout (init_point).
    /// Timeout de 5 s definido na implementação concreta (ADR-0005).
    /// </summary>
    Task<PreferenceCriadaResult> CriarPreferenceAsync(
        CriarPreferenceCommand command,
        CancellationToken ct = default);

    /// <summary>
    /// Consulta o pagamento na fonte (<c>GET v1/payments/{id}</c>, S32). O webhook só diz que algo mudou;
    /// o estado vale o que esta consulta devolver. Null quando o Mercado Pago não conhece o id (404).
    /// </summary>
    Task<PagamentoMercadoPago?> ConsultarPagamentoAsync(string pagamentoId, CancellationToken ct = default);

    /// <summary>
    /// Pagamentos com o <c>external_reference</c> dado, do mais novo para o mais antigo
    /// (<c>GET v1/payments/search</c>, S32): o job de cobrança pega webhook perdido antes de expirar.
    /// </summary>
    Task<IReadOnlyList<PagamentoMercadoPago>> BuscarPagamentosPorReferenciaAsync(
        string referenciaExterna, CancellationToken ct = default);

    /// <summary>
    /// Estorna o pagamento (<c>POST v1/payments/{id}/refunds</c>, S32) com <c>X-Idempotency-Key</c>.
    /// <paramref name="valor"/> null = estorno total. A chave é obrigatória e é de quem chama (I3.1): uma
    /// chave por intenção de estorno. Repetir a mesma chave não estorna duas vezes; dois estornos parciais
    /// de mesmo valor, com chaves diferentes, são dois reembolsos.
    /// </summary>
    Task<EstornoMercadoPagoResult> EstornarAsync(
        string pagamentoId, decimal? valor, string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Conta dona da credencial atual (<c>GET users/me</c>, campo <c>id</c>; I3.2). É o <c>collector_id</c> das
    /// cobranças que esta credencial cria e a única conta cujos pagamentos ela consegue consultar.
    /// </summary>
    Task<string> ObterContaRecebedoraAsync(CancellationToken ct = default);

    Task<EstornoMercadoPagoResult?> ConsultarEstornoAsync(string pagamentoId, string estornoId, CancellationToken ct = default);
    Task<IReadOnlyList<EstornoMercadoPagoResult>> ListarEstornosAsync(string pagamentoId, CancellationToken ct = default);

    /// <summary>
    /// Encerra a validade de uma preferência (<c>PUT checkout/preferences/{id}</c> com
    /// <c>expiration_date_to</c>, S32): usado na troca de forma de pagamento para o link antigo parar de aceitar.
    /// </summary>
    Task ExpirarPreferenciaAsync(string preferenceId, DateTime expiraEm, CancellationToken ct = default);
}
