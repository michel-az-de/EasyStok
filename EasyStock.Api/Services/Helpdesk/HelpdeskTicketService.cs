using System.Text.Json;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Api.Services.Helpdesk;

/// <summary>
/// Servico central de operacoes administrativas em tickets de helpdesk.
/// Cada operacao registra entrada em ticket_historico e (quando aplicavel)
/// dispara evento via outbox de notificacoes.
/// </summary>
public class HelpdeskTicketService(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser,
    ISlaResolver slaResolver,
    INotificadorService notificador)
{
    // virtual para permitir Substitute.For em testes (AutoTicketFalhaPagamentoTests).
    // Sem essa marcacao a classe seria mockada apenas via interface — overhead nao
    // justificado para um servico interno da camada Api.
    public virtual async Task<AdminTicket> AbrirAsync(AbrirAdminTicketCommand cmd, CancellationToken ct = default)
    {
        _ = await db.Empresas.FirstOrDefaultAsync(e => e.Id == cmd.EmpresaId, ct)
            ?? throw new KeyNotFoundException("Empresa nao encontrada.");

        // F9 — valida fatura pertence a empresa quando informada.
        Domain.Entities.Fatura? fatura = null;
        if (cmd.FaturaId.HasValue && cmd.FaturaId.Value != Guid.Empty)
        {
            fatura = await db.Faturas
                .FirstOrDefaultAsync(f => f.Id == cmd.FaturaId.Value && f.EmpresaId == cmd.EmpresaId, ct)
                ?? throw new KeyNotFoundException("Fatura nao encontrada para esta empresa.");
        }

        // Onda 1.1 — valida pedido pertence a empresa quando informado (espelha guard de Fatura).
        Domain.Entities.Pedido? pedido = null;
        if (cmd.PedidoId.HasValue && cmd.PedidoId.Value != Guid.Empty)
        {
            pedido = await db.Pedidos
                .FirstOrDefaultAsync(p => p.Id == cmd.PedidoId.Value && p.EmpresaId == cmd.EmpresaId, ct)
                ?? throw new KeyNotFoundException("Pedido nao encontrado para esta empresa.");
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
        ticket.PedidoId = pedido?.Id;

        db.AdminTickets.Add(ticket);
        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, autorId, TicketAcaoHistorico.Criado,
            metadadosJson: JsonSerializer.Serialize(new { ticket.Prioridade, ticket.Nivel, ticket.Categoria, faturaId = fatura?.Id, pedidoId = pedido?.Id })));

        // Vinculacao reversa: Fatura.TicketRelacionadoId aponta para o
        // primeiro ticket sobre ela (idempotente — se ja vinculada, mantem).
        if (fatura is not null && fatura.VinculaTicket(ticket.Id))
        {
            db.Faturas.Update(fatura);
        }

        // ADR-0030: TicketCriado nao e enfileirado no P0 — so teria destinatario de "fila"
        // (usuarioId=null -> Falhado garantido, poluindo o sinal de Falhado). Notificacao de
        // fila/nivel para o time de atendimento entra no P1-C (modelo de destinatario de fila).
        await db.CommitAsync();

        return ticket;
    }

    public async Task<AdminTicketMensagem> ResponderAsync(ResponderAdminTicketCommand cmd, CancellationToken ct = default)
    {
        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        if (ticket.Status == TicketStatus.Fechado)
            throw new RegraDeDominioVioladaException("Não é possível responder um ticket fechado. Reabra-o antes.");

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

        if (!cmd.Interno)
        {
            await notificador.EnfileirarEventoAsync(
                TipoEventoNotificacao.TicketRespondidoAdmin,
                ticket.EmpresaId,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    usuarioId = ticket.CriadoPorId,
                    autorNome = currentUser.UsuarioId.ToString()
                }),
                refEntidadeId: ticket.Id,
                ct: ct);
        }

        await db.CommitAsync();

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

        // Convite CSAT no fechamento — uma vez por ticket. Carimbo idempotente
        // evita reenvio caso ticket reabra (cliente respondeu) e feche de novo.
        var enviarConviteCsat = cmd.NovoStatus == TicketStatus.Fechado
            && ticket.ConviteCsatEnviadoEm is null
            && ticket.CriadoPorId.HasValue;
        if (enviarConviteCsat)
            ticket.ConviteCsatEnviadoEm = DateTime.UtcNow;

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.StatusAlterado,
            valorAntes: statusAntes.ToString(),
            valorDepois: cmd.NovoStatus.ToString()));

        // Onda 1.1 — trilha cruzada Pedido <-> Ticket. Quando ticket vinculado a
        // pedido eh resolvido, registra PedidoEvento "ticket_resolvido". Apenas
        // registro; nao altera estado do pedido (operador faz mutacao explicita
        // se a resolucao envolver reembolso/cancelamento).
        if (cmd.NovoStatus == TicketStatus.Resolvido && ticket.PedidoId.HasValue)
        {
            db.Set<Domain.Entities.PedidoEvento>().Add(new Domain.Entities.PedidoEvento
            {
                Id = Guid.NewGuid(),
                PedidoId = ticket.PedidoId.Value,
                Tipo = "ticket_resolvido",
                Detalhes = JsonSerializer.Serialize(new { ticketId = ticket.Id, titulo = ticket.Titulo }),
                UsuarioId = currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId,
                Origem = "api",
                OcorridoEm = DateTime.UtcNow
            });
        }

        await notificador.EnfileirarEventoAsync(
            TipoEventoNotificacao.TicketStatusAlterado,
            ticket.EmpresaId,
            payloadJson: JsonSerializer.Serialize(new { ticketId = ticket.Id, usuarioId = ticket.CriadoPorId, statusAntes = statusAntes.ToString(), statusDepois = cmd.NovoStatus.ToString() }),
            refEntidadeId: ticket.Id,
            ct: ct);

        if (enviarConviteCsat)
        {
            await notificador.EnfileirarEventoAsync(
                TipoEventoNotificacao.ConviteCsat,
                ticket.EmpresaId,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    usuarioId = ticket.CriadoPorId,
                    avaliarUrl = $"/api/helpdesk/tickets/{ticket.Id}/avaliacao"
                }),
                refEntidadeId: ticket.Id,
                ct: ct);
        }

        await db.CommitAsync();
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

        if (ticket.Status is TicketStatus.Resolvido or TicketStatus.Fechado)
            throw new RegraDeDominioVioladaException("Não é possível assumir um ticket resolvido ou fechado.");

        var antes = ticket.AtendenteId;
        ticket.AtendenteId = currentUser.UsuarioId;
        ticket.AlteradoEm = DateTime.UtcNow;
        if (ticket.Status == TicketStatus.Aberto)
            ticket.Status = TicketStatus.EmAtendimento;

        db.TicketHistoricos.Add(TicketHistorico.Criar(
            ticket.Id, currentUser.UsuarioId, TicketAcaoHistorico.AtendenteAtribuido,
            valorAntes: antes?.ToString(),
            valorDepois: currentUser.UsuarioId.ToString()));

        // ADR-0030: nao enfileira TicketAtribuido aqui — assumir = auto-atribuicao (o ator e o
        // proprio atendente); auto-notificacao seria ruido. Notifica so em AtribuirAsync.
        await db.CommitAsync();
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

        // Notifica o atendente designado — exceto auto-atribuicao (ator == destino), que seria ruido.
        if (cmd.AtendenteId != currentUser.UsuarioId)
        {
            await notificador.EnfileirarEventoAsync(
                TipoEventoNotificacao.TicketAtribuido,
                ticket.EmpresaId,
                payloadJson: JsonSerializer.Serialize(new { ticketId = ticket.Id, titulo = ticket.Titulo, usuarioId = cmd.AtendenteId }),
                refEntidadeId: ticket.Id,
                ct: ct);
        }

        await db.CommitAsync();
    }

    public async Task EncaminharAsync(EncaminharNivelCommand cmd, CancellationToken ct = default)
    {
        if (!currentUser.TemPermissao(Permissao.EncaminharTicketNivel))
            throw new UnauthorizedAccessException("Sem permissao para encaminhar ticket entre niveis.");

        var ticket = await db.AdminTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct)
            ?? throw new KeyNotFoundException("Ticket nao encontrado.");

        if (ticket.Status == TicketStatus.Fechado)
            throw new RegraDeDominioVioladaException("Não é possível encaminhar um ticket fechado.");

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

        // ADR-0030: TicketEncaminhadoNivel nao e enfileirado no P0 — destinatario seria a "fila"
        // do nivel destino (usuarioId=null -> Falhado). Notificacao de fila/nivel = P1-C.
        await db.CommitAsync();
    }
}
