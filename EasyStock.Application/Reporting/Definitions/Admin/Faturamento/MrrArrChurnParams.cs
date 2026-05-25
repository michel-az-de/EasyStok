namespace EasyStock.Application.Reporting.Definitions.Admin.Faturamento;

/// <summary>Parâmetros do relatório MRR/ARR/Churn — Admin SaaS.</summary>
public sealed record MrrArrChurnParams(
    /// <summary>Data inicial da apuração (inclusive). Default: início do mês corrente.</summary>
    DateOnly De,
    /// <summary>Data final da apuração (inclusive). Default: hoje.</summary>
    DateOnly Ate);
