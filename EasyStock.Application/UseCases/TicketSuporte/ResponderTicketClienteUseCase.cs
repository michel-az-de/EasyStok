using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    public sealed record ResponderTicketClienteCommand(Guid TicketId, string Resposta);

    public sealed class ResponderTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        public async Task ExecuteAsync(ResponderTicketClienteCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Resposta) || cmd.Resposta.Length > 5000)
                throw new UseCaseValidationException("Resposta inválida");

            var ticket = await ticketRepo.GetByIdAsync(
                currentUser.EmpresaId,
                cmd.TicketId,
                clienteId: currentUser.UsuarioId);

            if (ticket == null)
                throw new KeyNotFoundException("Ticket não encontrado");

            ticket.Mensagens.Add(new AdminTicketMensagem
            {
                Id = Guid.NewGuid(),
                TicketId = cmd.TicketId,
                Conteudo = cmd.Resposta,
                AutorId = currentUser.UsuarioId,
                IsAdmin = false,
                CriadoEm = DateTime.UtcNow
            });

            if (ticket.Status is TicketStatus.Resolvido or TicketStatus.Fechado)
                ticket.Status = TicketStatus.Aberto;

            await ticketRepo.UpdateAsync(ticket);
            await unitOfWork.CommitAsync();
        }
    }
}
