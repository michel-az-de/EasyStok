using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.UseCases.TicketSuporte
{
    public sealed record AbrirTicketClienteCommand(
        string Titulo,
        string Descricao,
        TicketCategoria Categoria);

    public sealed record AbrirTicketClienteResult(
        Guid TicketId,
        string Status,
        DateTime CriadoEm);

    public sealed class AbrirTicketClienteUseCase(
        IClienteTicketRepository ticketRepo,
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser)
    {
        public async Task<AbrirTicketClienteResult> ExecuteAsync(AbrirTicketClienteCommand cmd)
        {
            if (string.IsNullOrWhiteSpace(cmd.Titulo) || cmd.Titulo.Length > 200)
                throw new UseCaseValidationException("Título inválido");

            if (string.IsNullOrWhiteSpace(cmd.Descricao) || cmd.Descricao.Length > 5000)
                throw new UseCaseValidationException("Descrição inválida");

            var ticket = new AdminTicket
            {
                Id = Guid.NewGuid(),
                EmpresaId = currentUser.EmpresaId,
                Titulo = cmd.Titulo,
                Status = TicketStatus.Aberto,
                Prioridade = TicketPrioridade.Normal,
                Categoria = cmd.Categoria,
                CriadoPorId = currentUser.UsuarioId,
                CriadoEm = DateTime.UtcNow,
                AlteradoEm = DateTime.UtcNow
            };

            ticket.Mensagens.Add(new AdminTicketMensagem
            {
                Id = Guid.NewGuid(),
                TicketId = ticket.Id,
                Conteudo = cmd.Descricao,
                AutorId = currentUser.UsuarioId,
                IsAdmin = false,
                CriadoEm = DateTime.UtcNow
            });

            await ticketRepo.InsertAsync(ticket);
            await unitOfWork.CommitAsync();

            return new(ticket.Id, ticket.Status.ToString(), ticket.CriadoEm);
        }
    }
}
