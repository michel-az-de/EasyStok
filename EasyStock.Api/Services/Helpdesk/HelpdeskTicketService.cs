using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Servico central de operacoes administrativas em tickets de helpdesk.
/// Cada operacao registra entrada em ticket_historico e (quando aplicavel)
/// dispara evento via outbox de notificacoes.
/// </summary>
public sealed class HelpdeskTicketService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    SlaResolver slaResolver,
    INotificadorService notificador)
{
    public async Task<AdminTicket> AbrirAsync(AbrirAdminTicketCommand cmd, CancellationToken ct = default)
    {
        var empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == cmd.EmpresaId, ct)
            ?? throw new KeyNotFoundException("Empresa nao encontrada.");

        // F9 — valida fatura pertence a empresa quando informada.
        Domain.Entities.Fatura? fatura = null;
        if (cmd.FaturaId.HasValue && cmd.FaturaId.Value != Guid.Empty)
        {
            fatura = await db.Faturas
                .FirstOrDefaultAsync(f => f.Id == cmd.FaturaId.Value && f.EmpresaId == cmd.EmpresaId, ct)
                ?? throw new KeyNotFoundException("Fatura nao encontrada para esta empresa.");
        }

        var sla = await slaResolver.ResolverAsync(cmd.EmpresaId, cmd.Prioridade, ct: ct);

        // Em contexto de webhook (anonimo) currentUser.UsuarioId retorna Guid.Empty;
        // o FK p/ Usuarios falha. Normaliza para null quando nao autenticado.
        var autorId = currentUser.UsuarioId == Guid.Empty ? (Guid?)null : currentUser.UsuarioId;

        var ticket = AdminTicket.Criar(
            empresaId: cmd.EmpresaId,
            titulo: cmd.Titulo,
            descricao: cmd.Descricao,
            categoria: cmd.Categoria,
            prioridade: cmd.Prioridade,
            nivel: cmd.Nivel,
            prazoResposta: sla.PrazoResposta,
            prazoResolucao: sla.PrazoResolucao,
            criadoPorId: autorId);
        ticket.FaturaId = fatura?.Id;

        db.AdminTickets.Add(ticket);
        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, autorId, TicketAcaoHistorico.Criado,
            metadadosJson: JsonSerializer.Serialize(new { ticket.Prioridade, ticket.Nivel, ticket.Categoria, faturaId = fatura?.Id })));

        // Vinculacao reversa: Fatura.TicketRelacionadoId aponta para o
        // primeiro ticket sobre ela (idempotente — se ja vinculada, mantem).
        if (fatura is not null && fatura.VinculaTicket(ticket.Id))
        {
            db.Faturas.Update(fatura);
        }

        await db.CommitAsync();

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.TicketCriado,
            cmd.EmpresaId,
            usuarioDestinoId: null,
            payloadJson: JsonSerializer.Serialize(new
            {
                ticketId = ticket.Id,
                titulo = ticket.Titulo,
                prioridade = ticket.Prioridade.ToString(),
                nivel = ticket.Nivel.ToString(),
                empresaNome = empresa.Nome
            }),
            ct: ct);

        return ticket;
    }

    public async Task<AdminTicketMensagem> ResponderAsync(ResponderAdminTicketCommand cmd, CancellationToken ct = default)
    {
        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        if (cmd.Interno && !currentUser.TemPermissao(Permissao.ResponderTicketsInternos))
            throw new UnauthorizedAccessException("Sem permissao para comentario interno.");

        var mensagem = AdminTicketMensagem.Criar(ticket.Id, currentUser.UsuarioId, cmd.Conteudo, isAdmin: true, interno: cmd.Interno);
        db.AdminTicketMensagens.Add(mensagem);

        var agora = DateTime.UtcNow;
        var transicionou = false;

        if (!cmd.Interno && ticket.PrimeiraRespostaEm is null)
        {
            ticket.PrimeiraRespostaEm = agora;
        }

        if (!cmd.Interno && ticket.Status == TicketStatus.Aberto)
        {
            ticket.Status = TicketStatus.EmAtendimento;
            transicionou = true;
        }

        ticket.AlteradoEm = agora;

        // Vincular anexos pendentes (se enviados antes da mensagem) a essa mensagem.
        if (cmd.AnexoIds is { Count: > 0 })
        {
            var ids = cmd.AnexoIds.ToHashSet();
            var anexos = await db.TicketAnexos
                .Where(a => ids.Contains(a.Id) && a.TicketId == ticket.Id && a.MensagemId == null)
                .ToListAsync(ct);
            foreach (var a in anexos)
                a.MensagemId = mensagem.Id;
        }

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId,
            cmd.Interno ? TicketAcaoHistorico.Comentario : TicketAcaoHistorico.Comentario,
            metadadosJson: JsonSerializer.Serialize(new { interno = cmd.Interno, transicionou })));

        await db.CommitAsync();

        if (!cmd.Interno)
        {
            await notificador.PublicarEventoAsync(
                TipoEventoNotificacao.TicketRespondidoAdmin,
                ticket.EmpresaId,
                usuarioDestinoId: ticket.CriadoPorId,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    autorNome = currentUser.UsuarioId.ToString()
                }),
                ct: ct);
        }

        return mensagem;
    }

    public async Task AlterarStatusAsync(AlterarStatusTicketCommand cmd, CancellationToken ct = default)
    {
        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        var statusAntes = ticket.Status;
        if (statusAntes == cmd.NovoStatus) return;

        ticket.Status = cmd.NovoStatus;
        ticket.AlteradoEm = DateTime.UtcNow;
        if (cmd.NovoStatus == TicketStatus.Resolvido && ticket.ResolvidoEm is null)
            ticket.ResolvidoEm = DateTime.UtcNow;

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.StatusAlterado,
            valorAntes: statusAntes.ToString(),
            valorDepois: cmd.NovoStatus.ToString()));

        await db.CommitAsync();

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.TicketStatusAlterado,
            ticket.EmpresaId,
            usuarioDestinoId: ticket.CriadoPorId,
            payloadJson: JsonSerializer.Serialize(new { ticketId = ticket.Id, statusAntes = statusAntes.ToString(), statusDepois = cmd.NovoStatus.ToString() }),
            ct: ct);
    }

    public async Task AlterarPrioridadeAsync(AlterarPrioridadeTicketCommand cmd, CancellationToken ct = default)
    {
        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        var antes = ticket.Prioridade;
        if (antes == cmd.NovaPrioridade) return;

        ticket.Prioridade = cmd.NovaPrioridade;
        ticket.AlteradoEm = DateTime.UtcNow;

        // Recalcula prazos quando prioridade muda (ticket ainda nao resolvido).
        if (ticket.Status != TicketStatus.Resolvido && ticket.Status != TicketStatus.Fechado)
        {
            var sla = await slaResolver.ResolverAsync(ticket.EmpresaId, cmd.NovaPrioridade, ct: ct);
            ticket.PrazoResposta = sla.PrazoResposta;
            ticket.PrazoResolucao = sla.PrazoResolucao;
            ticket.UltimoAlerta50PctEm = null;
            ticket.UltimoAlerta80PctEm = null;
        }

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.PrioridadeAlterada,
            valorAntes: antes.ToString(),
            valorDepois: cmd.NovaPrioridade.ToString()));

        await db.CommitAsync();
    }

    public async Task AssumirAsync(AssumirTicketCommand cmd, CancellationToken ct = default)
    {
        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        var antes = ticket.AtendenteId;
        ticket.AtendenteId = currentUser.UsuarioId;
        ticket.AlteradoEm = DateTime.UtcNow;
        if (ticket.Status == TicketStatus.Aberto)
            ticket.Status = TicketStatus.EmAtendimento;

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.AtendenteAtribuido,
            valorAntes: antes?.ToString(),
            valorDepois: currentUser.UsuarioId.ToString()));

        await db.CommitAsync();

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.TicketAtribuido,
            ticket.EmpresaId,
            usuarioDestinoId: currentUser.UsuarioId,
            payloadJson: JsonSerializer.Serialize(new { ticketId = ticket.Id, titulo = ticket.Titulo }),
            ct: ct);
    }

    public async Task AtribuirAsync(AtribuirTicketCommand cmd, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarTickets))
            throw new UnauthorizedAccessException("Sem permissao para atribuir ticket.");

        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        var antes = ticket.AtendenteId;
        ticket.AtendenteId = cmd.AtendenteId;
        ticket.AlteradoEm = DateTime.UtcNow;

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.AtendenteAtribuido,
            valorAntes: antes?.ToString(),
            valorDepois: cmd.AtendenteId.ToString()));

        await db.CommitAsync();

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.TicketAtribuido,
            ticket.EmpresaId,
            usuarioDestinoId: cmd.AtendenteId,
            payloadJson: JsonSerializer.Serialize(new { ticketId = ticket.Id, titulo = ticket.Titulo }),
            ct: ct);
    }

    public async Task EncaminharAsync(EncaminharNivelCommand cmd, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.EncaminharTicketNivel))
            throw new UnauthorizedAccessException("Sem permissao para encaminhar ticket entre niveis.");

        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        var antes = ticket.Nivel;
        if (antes == cmd.NovoNivel) return;

        ticket.Nivel = cmd.NovoNivel;
        ticket.AtendenteId = null; // Volta para fila do nivel destino
        ticket.AlteradoEm = DateTime.UtcNow;

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.NivelEncaminhado,
            valorAntes: antes.ToString(),
            valorDepois: cmd.NovoNivel.ToString(),
            metadadosJson: cmd.Motivo is null ? null : JsonSerializer.Serialize(new { motivo = cmd.Motivo })));

        await db.CommitAsync();

        await notificador.PublicarEventoAsync(
            TipoEventoNotificacao.TicketEncaminhadoNivel,
            ticket.EmpresaId,
            usuarioDestinoId: null,
            payloadJson: JsonSerializer.Serialize(new
            {
                ticketId = ticket.Id,
                titulo = ticket.Titulo,
                nivelOrigem = antes.ToString(),
                nivelDestino = cmd.NovoNivel.ToString(),
                motivo = cmd.Motivo
            }),
            ct: ct);
    }
}
