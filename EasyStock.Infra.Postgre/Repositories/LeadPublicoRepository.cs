using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class LeadPublicoRepository(EasyStockDbContext db) : ILeadPublicoRepository
{
    public Task AddAsync(LeadPublico lead, CancellationToken ct = default)
    {
        db.LeadsPublicos.Add(lead);
        return Task.CompletedTask;
    }

    public Task<LeadPublico?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.LeadsPublicos.FirstOrDefaultAsync(l => l.Id == id, ct);

    public Task UpdateAsync(LeadPublico lead, CancellationToken ct = default)
    {
        db.LeadsPublicos.Update(lead);
        return Task.CompletedTask;
    }

    public Task<int> ContarPorIpRecenteAsync(string ip, TimeSpan janela, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ip)) return Task.FromResult(0);
        var desde = DateTime.UtcNow - janela;
        return db.LeadsPublicos
            .Where(l => l.IpOrigem == ip && l.CriadoEm >= desde)
            .CountAsync(ct);
    }

    public async Task<(IReadOnlyList<LeadPublico> Items, int Total)> ListarPaginadoAsync(
        OrigemLead? origem = null,
        bool? processado = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = db.LeadsPublicos.AsNoTracking();

        if (origem.HasValue) query = query.Where(l => l.Origem == origem.Value);
        if (processado.HasValue)
            query = processado.Value
                ? query.Where(l => l.ProcessadoEm != null)
                : query.Where(l => l.ProcessadoEm == null);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(l => l.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
