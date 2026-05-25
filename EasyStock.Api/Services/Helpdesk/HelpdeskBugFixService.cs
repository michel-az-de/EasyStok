using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Cria um ticket tecnico (Categoria=BugFixDev) ligado ao ticket original via OrigemTicketId,
/// com metadados tecnicos (severidade/componente/stack) na tabela admin_ticket_tecnico_meta.
/// Notifica usuarios com Permissao=GerarBugFix (papel desenvolvedor).
/// </summary>
public sealed class HelpdeskBugFixService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    ISlaResolver slaResolver,
    INotificadorService notificador)
{
    public async Task<AdminTicket> GerarAsync(GerarBugFixCommand cmd, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.GerarBugFix))
            throw new UnauthorizedAccessException("Sem permissao para gerar bug-fix.");

        var origem = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketOrigemId, ct)
            ?? throw new KeyNotFoundException("Ticket de origem nao encontrado.");

        // Bug-fix sempre na maior prioridade da origem (no minimo Alta) e nivel N4.
        var prioridade = origem.Prioridade == TicketPrioridade.Critica ? TicketPrioridade.Critica : TicketPrioridade.Alta;
        var sla = await slaResolver.ResolverAsync(origem.EmpresaId, prioridade, ct: ct);

        var ticket = AdminTicket.Criar(
            empresaId: origem.EmpresaId,
            titulo: cmd.Titulo,
            descricao: cmd.Descricao,
            categoria: TicketCategoria.BugFixDev,
            prioridade: prioridade,
            nivel: NivelAtendimento.N4,
            prazoResposta: sla.PrazoResposta,
            prazoResolucao: sla.PrazoResolucao,
            origemTicketId: origem.Id,
            criadoPorId: currentUser.UsuarioId);

        var meta = AdminTicketTecnicoMeta.Criar(
            ticketId: ticket.Id,
            severidadeTecnica: cmd.SeveridadeTecnica,
            componenteAfetado: cmd.ComponenteAfetado,
            stackTrace: cmd.StackTrace);

        db.AdminTickets.Add(ticket);
        db.AdminTicketTecnicoMetas.Add(meta);

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            origem.Id, currentUser.UsuarioId, TicketAcaoHistorico.BugFixGerado,
            valorDepois: ticket.Id.ToString(),
            metadadosJson: JsonSerializer.Serialize(new { severidade = cmd.SeveridadeTecnica, cmd.ComponenteAfetado })));

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.Criado,
            metadadosJson: JsonSerializer.Serialize(new { origem = origem.Id })));

        await db.CommitAsync();

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.BugFixCriado,
            origem.EmpresaId,
            usuarioDestinoId: null,
            payloadJson: JsonSerializer.Serialize(new
            {
                ticketId = ticket.Id,
                ticketOrigemId = origem.Id,
                titulo = ticket.Titulo,
                severidade = cmd.SeveridadeTecnica,
                componente = cmd.ComponenteAfetado
            }),
            ct: ct);

        return ticket;
    }
}
