namespace EasyStock.Application.Reporting.Definitions.Fiscal.MapMensal;

/// <summary>
/// Parâmetros do relatório "MAP — Mapa Resumo NFC-e" (mensal).
/// </summary>
public sealed record MapMensalParams(
    /// <summary>Primeiro dia do mês de competência.</summary>
    DateOnly De,
    /// <summary>Último dia do mês de competência (inclusive).</summary>
    DateOnly Ate);
