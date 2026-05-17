namespace EasyStock.Application.Reporting.Definitions.Admin.Faturamento;

/// <summary>Parâmetros do relatório de inadimplência — Admin SaaS.</summary>
public sealed record InadimplenciaParams(
    /// <summary>Data de referência para calcular atraso. Default: hoje.</summary>
    DateOnly DataReferencia,
    /// <summary>Filtrar somente faturas com atraso mínimo N dias. Default: 0 (todas vencidas).</summary>
    int AtrasoMinimoEmDias = 0);
