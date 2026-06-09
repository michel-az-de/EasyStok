using System.Text.Json;
using EasyStock.Application.Ports.Output.Helpdesk;
using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Enums.Notifications;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    public sealed record ResponderTicketClienteCommand(Guid TicketId, string Resposta);

    public sealed class ResponderTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        ISlaResolver slaResolver,
        INotificadorService notificador,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        public async Task ExecuteAsync(ResponderTicketClienteCommand cmd, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(cmd.Resposta) || cmd.Resposta.Length > 5000)
                throw new UseCaseValidationException("Resposta inválida");

            var ticket = await ticketRepo.GetByIdAsync(
                currentUser.EmpresaId,
                cmd.TicketId,
                clienteId: currentUser.UsuarioId);

            if (ticket == null)
                throw new KeyNotFoundException("Ticket não encontrado");

            var agora = DateTime.UtcNow;
            var statusAntes = ticket.Status;

            ticket.Mensagens.Add(AdminTicketMensagem.Criar(
                ticketId: cmd.TicketId,
                autorId: currentUser.UsuarioId,
                conteudo: cmd.Resposta,
                isAdmin: false));
            ticket.AlteradoEm = agora;

            // Reabertura: cliente respondendo a ticket Resolvido/Fechado reabre o
            // atendimento. SLA precisa ser recalculado — senao prazo antigo (ja
            // estourado) faria ticket nascer "violado" no SlaMonitor.
            var reaberto = false;
            if (ticket.Status is TicketStatus.Resolvido or TicketStatus.Fechado)
            {
                ticket.Status = TicketStatus.Aberto;
                ticket.ResolvidoEm = null;
                ticket.SlaRespostaViolado = false;
                ticket.SlaResolucaoViolado = false;
                ticket.UltimoAlerta50PctEm = null;
                ticket.UltimoAlerta80PctEm = null;
                ticket.PrimeiraRespostaEm = null;

                var sla = await slaResolver.ResolverAsync(ticket.EmpresaId, ticket.Prioridade, agora, ct);
                ticket.PrazoResposta = sla.PrazoResposta;
                ticket.PrazoResolucao = sla.PrazoResolucao;
                reaberto = true;
            }

            await ticketRepo.UpdateAsync(ticket);
            await ticketRepo.AddHistoricoAsync(TicketHistorico.Criar(
                ticketId: ticket.Id,
                autorId: currentUser.UsuarioId,
                acao: TicketAcaoHistorico.Comentario,
                metadadosJson: JsonSerializer.Serialize(new
                {
                    porCliente = true,
                    reaberto,
                    statusAntes = statusAntes.ToString()
                })));

            if (reaberto)
            {
                await ticketRepo.AddHistoricoAsync(TicketHistorico.Criar(
                    ticketId: ticket.Id,
                    autorId: currentUser.UsuarioId,
                    acao: TicketAcaoHistorico.StatusAlterado,
                    valorAntes: statusAntes.ToString(),
                    valorDepois: ticket.Status.ToString()));
            }

            await notificador.EnfileirarEventoAsync(
                TipoEventoNotificacao.TicketRespondidoCliente,
                ticket.EmpresaId,
                payloadJson: JsonSerializer.Serialize(new
                {
                    ticketId = ticket.Id,
                    titulo = ticket.Titulo,
                    usuarioId = ticket.AtendenteId,
                    reaberto,
                    autorId = currentUser.UsuarioId
                }),
                refEntidadeId: ticket.Id,
                ct: ct);

            await unitOfWork.CommitAsync();
        }
    }
}
