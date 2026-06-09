using System.Text.Json;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Cria um ticket tecnico (Categoria=BugFixDev) ligado ao ticket original via OrigemTicketId,
/// com metadados tecnicos (severidade/componente/stack) na tabela admin_ticket_tecnico_meta.
/// Notifica usuarios com Permissao=GerarBugFix (papel desenvolvedor).
/// </summary>
public sealed class HelpdeskBugFixService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    ISlaResolver slaResolver)
{
    public async Task<AdminTicket> GerarAsync(GerarBugFixCommand cmd, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.GerarBugFix))
            throw new UnauthorizedAccessException("Sem permissao para gerar bug-fix.");

        var origem = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketOrigemId, ct)
            ?? throw new KeyNotFoundException("Ticket de origem nao encontrado.");

        // BUG-03: respeitar a severidade ESCOLHIDA no modal. Antes ignorava cmd.SeveridadeTecnica
        // e usava sempre a prioridade da origem (no minimo Alta) -> "Media" virava "Alta". Decisao
        // (Felipe): a escolha do operador manda (pode rebaixar uma origem Critica conscientemente).
        var prioridade = MapearSeveridade(cmd.SeveridadeTecnica);
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

        // ADR-0030: BugFixCriado nao e enfileirado no P0 — destinatario seria o time de dev
        // (usuarioId=null -> Falhado garantido). Notificacao do time de dev = P1-C.
        await db.CommitAsync();

        return ticket;
    }

    // BUG-03: mapeia a severidade tecnica escolhida -> prioridade do ticket de bug-fix.
    // "Media" -> Normal (o enum TicketPrioridade nao tem "Media"). Tolerante a acento.
    // Default Alta (bug de dev sem severidade explicita assume-se relevante).
    private static TicketPrioridade MapearSeveridade(string? severidade) =>
        (severidade ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "critica" or "crítica" => TicketPrioridade.Critica,
            "alta" => TicketPrioridade.Alta,
            "media" or "média" or "normal" => TicketPrioridade.Normal,
            "baixa" => TicketPrioridade.Baixa,
            _ => TicketPrioridade.Alta,
        };
}
