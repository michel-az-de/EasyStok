namespace EasyStock.Application.Reporting.Definitions.Admin.Tickets;

/// <summary>Parâmetros do relatório de SLA violado — Admin SaaS.</summary>
public sealed record SlaVioladoParams(
    /// <summary>Data inicial (inclusive).</summary>
    DateOnly De,
    /// <summary>Data final (inclusive).</summary>
    DateOnly Ate);
