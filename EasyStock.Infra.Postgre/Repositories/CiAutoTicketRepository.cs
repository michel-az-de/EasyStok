using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre de apoio aos auto-tickets de CI (F7).
/// </summary>
public sealed class CiAutoTicketRepository(EasyStockDbContext db) : ICiAutoTicketRepository
{
    public async Task<Guid?> EncontrarAbertoHojeAsync(
        Guid empresaId, string titlePrefix, DateTime hojeUtc, CancellationToken ct = default)
        => await db.AdminTickets
            .Where(t => t.EmpresaId == empresaId
                     && t.Categoria == TicketCategoria.BugFixDev
                     && t.CriadoEm >= hojeUtc
                     && t.Status != TicketStatus.Resolvido
                     && t.Status != TicketStatus.Fechado
                     && t.Titulo.StartsWith(titlePrefix))
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(ct);

    public async Task AnexarReincidenciaAsync(Guid ticketId, string metadadosJson, CancellationToken ct = default)
    {
        var historico = TicketHistorico.Criar(
            ticketId, autorId: null, TicketAcaoHistorico.Comentario, metadadosJson);
        db.TicketHistoricos.Add(historico);

        var ticket = await db.AdminTickets.FindAsync([ticketId], ct);
        if (ticket is not null)
            ticket.AlteradoEm = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
    }
}
