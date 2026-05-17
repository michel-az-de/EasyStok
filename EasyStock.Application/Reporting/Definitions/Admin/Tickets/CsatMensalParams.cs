namespace EasyStock.Application.Reporting.Definitions.Admin.Tickets;

/// <summary>Parâmetros do relatório de CSAT mensal — Admin SaaS.</summary>
public sealed record CsatMensalParams(
    /// <summary>Data inicial (inclusive).</summary>
    DateOnly De,
    /// <summary>Data final (inclusive).</summary>
    DateOnly Ate);
