using EasyStock.Domain.Enums;

namespace EasyStock.Api.Services.Helpdesk;

public sealed record AbrirAdminTicketCommand(
    Guid EmpresaId,
    string Titulo,
    string Descricao,
    TicketCategoria Categoria,
    TicketPrioridade Prioridade,
    NivelAtendimento Nivel,
    Guid? AnexoIds);

public sealed record ResponderAdminTicketCommand(
    Guid TicketId,
    string Conteudo,
    bool Interno,
    IReadOnlyList<Guid>? AnexoIds);

public sealed record AlterarStatusTicketCommand(Guid TicketId, TicketStatus NovoStatus);
public sealed record AlterarPrioridadeTicketCommand(Guid TicketId, TicketPrioridade NovaPrioridade);
public sealed record AssumirTicketCommand(Guid TicketId);
public sealed record AtribuirTicketCommand(Guid TicketId, Guid AtendenteId);
public sealed record EncaminharNivelCommand(Guid TicketId, NivelAtendimento NovoNivel, string? Motivo);

public sealed record GerarBugFixCommand(
    Guid TicketOrigemId,
    string Titulo,
    string Descricao,
    string SeveridadeTecnica,
    string? ComponenteAfetado,
    string? StackTrace);

public sealed record AnexarArquivoCommand(
    Guid TicketId,
    Guid? MensagemId,
    string NomeArquivo,
    string ContentType,
    byte[] Conteudo,
    bool IsAdmin);

public sealed record RevelarClienteCommand(Guid EmpresaId, string Motivo, Guid? TicketIdContexto);

public sealed record SalvarSlaConfigItem(
    Guid? EmpresaId,
    Guid? PlanoId,
    TicketPrioridade Prioridade,
    int MinutosResposta,
    int MinutosResolucao);
