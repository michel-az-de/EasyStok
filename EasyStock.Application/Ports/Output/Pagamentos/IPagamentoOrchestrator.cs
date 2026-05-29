using EasyStock.Domain.Enums.Pagamentos;

namespace EasyStock.Application.Ports.Output.Pagamentos;

/// <summary>
/// Orquestrador central de cobrancas. Encapsula:
/// <list type="bullet">
///   <item>Consulta da rota via <see cref="IPagamentoGatewayRouter"/>.</item>
///   <item>Idempotencia da intencao via <c>FaturaPagamento.ClientIdempotencyKey</c>.</item>
///   <item>Criacao do <c>PaymentAttempt</c> antes de chamar gateway (transacional).</item>
///   <item>Audit trail via <c>PaymentAttemptEvent</c>.</item>
///   <item>Atualizacao de <c>FaturaPagamento.TotalTentativas</c> e <c>UltimaErrorCategory</c>.</item>
/// </list>
///
/// <para>
/// <b>Onda P0</b>: tenta APENAS o primeiro provedor do <see cref="RoutingPlan"/>;
/// se falhar, retorna falha sem fallback. Em P1, itera todos os provedores do plan
/// com retry (Polly) e fallback automatico, alimentando <c>IGatewayHealthStore</c>.
/// </para>
///
/// <para>
/// <b>Use cases</b> que hoje chamam <see cref="IPagamentoGatewayRouter.Resolver"/>
/// direto + <see cref="IPagamentoGateway.CriarAsync"/> devem migrar para esta
/// interface — o orchestrator garante consistencia (attempt + audit) e prepara
/// terreno para fallback.
/// </para>
/// </summary>
public interface IPagamentoOrchestrator
{
    /// <summary>
    /// Cria cobranca para a fatura. Em P0: tenta UM provedor (primeiro do plan).
    /// Em P1: tenta cadeia completa com fallback.
    /// </summary>
    /// <param name="fatura">Fatura ja emitida (Total &gt; 0).</param>
    /// <param name="metodo">"pix" | "boleto" | "cartao" | etc.</param>
    /// <param name="clientIdempotencyKey">
    /// Header <c>Idempotency-Key</c> opcional do cliente. Se vier e ja existir
    /// um <c>FaturaPagamento</c> ativo com mesma key, retorna o existente
    /// (replay seguro).
    /// </param>
    /// <param name="ct">CancellationToken.</param>
    Task<OrquestracaoResult> CriarComFallbackAsync(
        Fatura fatura,
        string metodo,
        string? clientIdempotencyKey = null,
        CancellationToken ct = default);
}

/// <summary>Resultado da orquestracao.</summary>
public sealed record OrquestracaoResult(
    bool Sucesso,
    InstrucaoPagamento? Instrucao,
    Guid? FaturaPagamentoId,
    StatusFaturaPagamento StatusFaturaPagamento,
    IReadOnlyList<PaymentAttemptResumo> Tentativas,
    string? MotivoFalhaFinal);

/// <summary>Resumo de uma tentativa para retorno ao caller (sem expor entity).</summary>
public sealed record PaymentAttemptResumo(
    Guid Id,
    string Provedor,
    int Tentativa,
    StatusPaymentAttempt Status,
    ErrorCategory? ErroCategoria,
    string? ErroMensagem);
