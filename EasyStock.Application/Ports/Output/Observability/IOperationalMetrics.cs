namespace EasyStock.Application.Ports.Output.Observability;

/// <summary>
/// Porta de saída para métricas operacionais. O adapter (na camada Api) emite via
/// OpenTelemetry; a interface não expõe nenhum tipo de OTel para que a Application
/// permaneça independente da infraestrutura de observabilidade.
///
/// Hoje expõe apenas o contador de falhas 5xx — o único evento com um ponto de emissão
/// honesto (o GlobalExceptionHandler, fora de transação/retry). As métricas de NEGÓCIO
/// (entradas/saídas/reposições/vendas) ficam adiadas: a baixa/entrada de estoque acontece
/// em múltiplos caminhos e dentro de transação com retry (IExecutionStrategy), então a
/// contagem honesta exige um seam pós-commit sobre a persistência de MovimentacaoEstoque
/// (ver ADR-0036).
/// </summary>
public interface IOperationalMetrics
{
    /// <summary>
    /// Incrementa o contador de falhas de operação, rotulado pelo código do erro 5xx
    /// (ex.: <c>INTERNAL_ERROR</c>, <c>NOT_SUPPORTED</c> — domínio fechado para 5xx).
    /// </summary>
    void IncrementFalhasOperacao(string code);
}
