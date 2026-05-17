namespace EasyStock.Application.Reporting.Definitions.Fiscal.CancelamentosInutilizacoes;

/// <summary>
/// Parâmetros do relatório "Cancelamentos e inutilizações".
/// </summary>
public sealed record CancelamentosInutilizacoesParams(
    DateOnly De,
    DateOnly Ate);
