namespace EasyStock.Application.Reporting.Definitions.Admin.Faturamento;

/// <summary>
/// Linha do relatório MRR/ARR/Churn agrupada por mês.
/// Métricas SaaS de recorrência financeira.
/// </summary>
public sealed record MrrArrChurnRow(
    /// <summary>Ano-mês de referência (yyyy-MM).</summary>
    string Competencia,
    /// <summary>Assinaturas ativas no final do mês.</summary>
    int AssinaturasAtivas,
    /// <summary>Assinaturas canceladas no mês.</summary>
    int AssinaturasCanceladas,
    /// <summary>Assinaturas suspensas no mês.</summary>
    int AssinaturasSuspensas,
    /// <summary>Novas assinaturas iniciadas no mês.</summary>
    int AssinaturasNovas,
    /// <summary>MRR calculado sobre assinaturas ativas (soma dos planos mensais).</summary>
    decimal Mrr,
    /// <summary>ARR = MRR × 12.</summary>
    decimal Arr,
    /// <summary>Churn Rate percentual = canceladas / ativas_inicio * 100.</summary>
    decimal ChurnRatePercent,
    /// <summary>Receita efetivamente paga no mês (faturas com DataPagamentoTotal no mês).</summary>
    decimal ReceitaRealizada,
    /// <summary>Ticket médio das faturas pagas no mês.</summary>
    decimal TicketMedio);
