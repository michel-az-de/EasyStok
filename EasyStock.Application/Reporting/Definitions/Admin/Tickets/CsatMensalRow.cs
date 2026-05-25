namespace EasyStock.Application.Reporting.Definitions.Admin.Tickets;

/// <summary>Linha do relatório de CSAT mensal — um ticket avaliado.</summary>
public sealed record CsatMensalRow(
    Guid TicketId,
    string EmpresaNome,
    string Categoria,
    string Prioridade,
    DateTime CriadoEm,
    DateTime? ResolvidoEm,
    int? NotaCsat,
    DateTime? AvaliadoEm,
    bool ConviteEnviado,
    DateTime? ConviteEnviadoEm);
