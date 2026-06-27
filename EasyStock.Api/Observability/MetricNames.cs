namespace EasyStock.Api.Observability;

/// <summary>
/// Nomes canônicos do meter e dos instrumentos de métrica. Fonte ÚNICA usada por:
///   • <see cref="MetricsService"/> (<c>meterFactory.Create</c> / <c>CreateCounter</c>),
///   • o registro <c>.AddMeter(...)</c> em <c>AddEasyStockObservability</c>, e
///   • os testes.
/// Centralizar evita drift entre o nome com que o instrumento é criado e o nome inscrito
/// no MeterProvider — divergência que quebraria o export OTLP em silêncio (teste verde,
/// métrica invisível em produção).
/// </summary>
public static class MetricNames
{
    /// <summary>Nome do meter da API. Deve casar com o <c>.AddMeter(...)</c> do MeterProvider.</summary>
    public const string MeterName = "EasyStock.Api";

    /// <summary>Contador de falhas de operação (respostas 5xx tratadas pelo GlobalExceptionHandler).</summary>
    public const string FalhasOperacaoTotal = "falhas_operacao_total";
}
