using EasyStock.Domain.Enums;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Comando de abertura de ticket admin. <c>FaturaId</c> e opcional (F9) e
/// <c>PedidoId</c> e opcional (Onda 1.1): quando informados, vinculam o
/// ticket bidirecionalmente a uma Fatura ou Pedido existente da mesma empresa.
/// </summary>
public sealed record AbrirAdminTicketCommand(
    Guid EmpresaId,
    string Titulo,
    string Descricao,
    TicketCategoria Categoria,
    TicketPrioridade Prioridade,
    NivelAtendimento Nivel,
    Guid? FaturaId = null,
    Guid? PedidoId = null);

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
