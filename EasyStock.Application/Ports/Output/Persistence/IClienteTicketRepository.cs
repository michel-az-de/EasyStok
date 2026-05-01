using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IClienteTicketRepository
    {
        Task<AdminTicket?> GetByIdAsync(Guid empresaId, Guid ticketId, Guid? clienteId = null);
        Task<(IEnumerable<AdminTicket>, int)> GetMeusTicketsAsync(
            Guid empresaId,
            Guid clienteId,
            int page,
            int pageSize,
            string? status = null,
            string? categoria = null);
        Task InsertAsync(AdminTicket ticket);
        Task UpdateAsync(AdminTicket ticket);
    }
}
