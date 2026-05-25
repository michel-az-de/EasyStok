namespace EasyStock.Application.Reporting.Definitions.Fiscal.TotalizadoresFiscais;

/// <summary>
/// Parâmetros do relatório "Totalizadores fiscais por CFOP/CST/NCM".
/// </summary>
public sealed record TotalizadoresFiscaisParams(
    DateOnly De,
    DateOnly Ate);
