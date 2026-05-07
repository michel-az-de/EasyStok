using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class ClienteTicketRepository(EasyStockDbContext db) : IClienteTicketRepository
    {
        public async Task<AdminTicket?> GetByIdAsync(Guid empresaId, Guid ticketId, Guid? clienteId = null)
        {
            var query = db.AdminTickets
                .AsNoTracking()
                .Where(t => t.Id == ticketId && t.EmpresaId == empresaId);

            if (clienteId.HasValue)
                query = query.Where(t => t.CriadoPorId == clienteId);

            return await query
                .Include(t => t.Mensagens.Where(m => !m.Interno))
                .Include(t => t.CriadoPor)
                .FirstOrDefaultAsync();
        }

        public async Task<(IEnumerable<AdminTicket>, int)> GetMeusTicketsAsync(
            Guid empresaId,
            Guid clienteId,
            int page,
            int pageSize,
            string? status = null,
            string? categoria = null)
        {
            var query = db.AdminTickets
                .AsNoTracking()
                .Where(t => t.EmpresaId == empresaId && t.CriadoPorId == clienteId);

            if (status != null && Enum.TryParse<TicketStatus>(status, out var s))
                query = query.Where(t => t.Status == s);

            if (categoria != null && Enum.TryParse<TicketCategoria>(categoria, out var c))
                query = query.Where(t => t.Categoria == c);

            var total = await query.CountAsync();
            var tickets = await query
                .OrderByDescending(t => t.CriadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (tickets, total);
        }

        public async Task InsertAsync(AdminTicket ticket)
        {
            await db.AdminTickets.AddAsync(ticket);
        }

        public Task UpdateAsync(AdminTicket ticket)
        {
            db.AdminTickets.Update(ticket);
            return Task.CompletedTask;
        }
    }
}
