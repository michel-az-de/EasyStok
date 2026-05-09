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

        /// <summary>
        /// Persiste entrada de historico do ticket (acoes auditaveis: criacao, comentario,
        /// mudanca de status, etc). Usado pelo fluxo cliente em paralelo aos use cases de
        /// abertura e resposta para garantir trilha de auditoria E2E (paridade com fluxo admin).
        /// </summary>
        Task AddHistoricoAsync(TicketHistorico historico);
    }
}
