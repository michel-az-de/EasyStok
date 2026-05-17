namespace EasyStock.Application.Reporting.Definitions.Admin.Tickets;

/// <summary>Linha do relatório de SLA violado — um ticket com violação de SLA.</summary>
public sealed record SlaVioladoRow(
    Guid     TicketId,
    string   Titulo,
    string   EmpresaNome,
    string   Categoria,
    string   Prioridade,
    DateTime CriadoEm,
    bool     SlaRespostaViolado,
    bool     SlaResolucaoViolado,
    DateTime? PrazoResposta,
    DateTime? PrazoResolucao,
    DateTime? PrimeiraRespostaEm,
    DateTime? ResolvidoEm,
    int      MinutosAtrasoResposta,
    int      MinutosAtrasoResolucao);
