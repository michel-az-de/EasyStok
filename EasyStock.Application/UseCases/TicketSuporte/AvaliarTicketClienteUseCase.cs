using System.Text.Json;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    public sealed record AvaliarTicketClienteCommand(
        Guid TicketId,
        int Nota,
        string? Comentario);

    /// <summary>
    /// Cliente avalia atendimento (CSAT 1..5) de um ticket Resolvido ou Fechado.
    /// Idempotente: segunda avaliacao no mesmo ticket atualiza nota+comentario+carimbo.
    /// </summary>
    public sealed class AvaliarTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        public async Task ExecuteAsync(AvaliarTicketClienteCommand cmd, CancellationToken ct = default)
        {
            if (cmd.Nota < 1 || cmd.Nota > 5)
                throw new UseCaseValidationException("Nota deve estar entre 1 e 5.");

            if (cmd.Comentario is { Length: > 500 })
                throw new UseCaseValidationException("Comentário excede 500 caracteres.");

            var ticket = await ticketRepo.GetByIdAsync(
                currentUser.EmpresaId,
                cmd.TicketId,
                clienteId: currentUser.UsuarioId);

            if (ticket is null)
                throw new KeyNotFoundException("Ticket não encontrado");

            // CSAT so faz sentido apos resolucao — cliente nao avalia ticket que ainda
            // nao foi atendido. Aceita Resolvido OU Fechado para janela mais ampla.
            if (ticket.Status is not (TicketStatus.Resolvido or TicketStatus.Fechado))
                throw new UseCaseValidationException(
                    "Avaliação só é permitida em tickets Resolvidos ou Fechados.");

            var notaAnterior = ticket.NotaCsat;
            ticket.NotaCsat = cmd.Nota;
            ticket.ComentarioCsat = string.IsNullOrWhiteSpace(cmd.Comentario)
                ? null
                : cmd.Comentario.Trim();
            ticket.AvaliadoEm = DateTime.UtcNow;

            await ticketRepo.UpdateAsync(ticket);
            await ticketRepo.AddHistoricoAsync(TicketHistorico.Criar(
                ticketId: ticket.Id,
                autorId: currentUser.UsuarioId,
                acao: TicketAcaoHistorico.Comentario,
                valorAntes: notaAnterior?.ToString(),
                valorDepois: cmd.Nota.ToString(),
                metadadosJson: JsonSerializer.Serialize(new { csat = true })));

            await unitOfWork.CommitAsync();
        }
    }
}
