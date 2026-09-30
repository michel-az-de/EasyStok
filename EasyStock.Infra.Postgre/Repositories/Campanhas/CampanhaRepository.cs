using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Domain.Entities.Campanhas;
using EasyStock.Domain.Enums.Campanhas;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Campanhas;

/// <summary>Campanhas (S28). <c>EmpresaId</c> no WHERE além do filtro global e do RLS (ADR-0010).</summary>
public sealed class CampanhaRepository(EasyStockDbContext db) : ICampanhaRepository
{
    public Task AddAsync(Campanha campanha, CancellationToken ct = default) =>
        db.Campanhas.AddAsync(campanha, ct).AsTask();

    public Task<Campanha?> ObterAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.Campanhas.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public async Task<IReadOnlyList<Campanha>> ListarAsync(
        Guid empresaId, StatusCampanha? status, int limite, CancellationToken ct = default)
    {
        var query = db.Campanhas.AsNoTracking().Where(c => c.EmpresaId == empresaId);
        if (status is { } s) query = query.Where(c => c.Status == s);
        return await query.OrderByDescending(c => c.CriadaEm).Take(Math.Clamp(limite, 1, 500)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CampanhaDestinatario>> ListarPendentesAsync(
        Guid empresaId, Guid campanhaId, CancellationToken ct = default) =>
        await db.CampanhaDestinatarios
            .Where(d => d.EmpresaId == empresaId && d.CampanhaId == campanhaId
                && d.Status == StatusCampanhaDestinatario.Pendente)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CampanhaDestinatario>> ListarDestinatariosAsync(
        Guid empresaId, Guid campanhaId, CancellationToken ct = default) =>
        await db.CampanhaDestinatarios
            .Where(d => d.EmpresaId == empresaId && d.CampanhaId == campanhaId)
            .ToListAsync(ct);

    public Task AddDestinatariosAsync(IEnumerable<CampanhaDestinatario> destinatarios, CancellationToken ct = default) =>
        db.CampanhaDestinatarios.AddRangeAsync(destinatarios, ct);

    public void RemoverDestinatarios(IEnumerable<CampanhaDestinatario> destinatarios) =>
        db.CampanhaDestinatarios.RemoveRange(destinatarios);
}
