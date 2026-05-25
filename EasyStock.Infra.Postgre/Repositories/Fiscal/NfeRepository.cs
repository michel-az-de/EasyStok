using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Fiscal;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories.Fiscal;

public sealed class NfeRepository(EasyStockDbContext db) : INfeRepository
{
    public Task<NfeDocumento?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.NfeDocumentos.FirstOrDefaultAsync(n => n.EmpresaId == empresaId && n.Id == id, ct);

    public Task<NfeDocumento?> GetByIdWithDetailsAsync(Guid empresaId, Guid id, CancellationToken ct = default) =>
        db.NfeDocumentos
            .Include(n => n.Itens)
            .Include(n => n.Eventos)
            .FirstOrDefaultAsync(n => n.EmpresaId == empresaId && n.Id == id, ct);

    public Task<NfeDocumento?> FindByChaveAcessoAsync(string chaveAcesso, CancellationToken ct = default) =>
        db.NfeDocumentos
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(n => n.ChaveAcesso == chaveAcesso, ct);

    // TODO F1.5: FindByIdempotencyKeyAsync — requer coluna IdempotencyKey via migration AddNfeF1RepoIndexes.

    public async Task<(IEnumerable<NfeDocumento> items, int total)> GetByEmpresaAsync(
        Guid empresaId,
        int page,
        int pageSize,
        StatusNfe? status = null,
        DateTime? desde = null,
        DateTime? ate = null,
        string? search = null,
        CancellationToken ct = default)
    {
        var query = db.NfeDocumentos.AsNoTracking()
            .Where(n => n.EmpresaId == empresaId);

        if (status.HasValue) query = query.Where(n => n.Status == status.Value);
        if (desde.HasValue) query = query.Where(n => n.CriadoEm >= desde.Value);
        if (ate.HasValue) query = query.Where(n => n.CriadoEm <= ate.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var termo = search.Trim();
            query = query.Where(n =>
                (n.ChaveAcesso != null && EF.Functions.ILike(n.ChaveAcesso, $"%{termo}%")) ||
                (n.ProtocoloAutorizacao != null && EF.Functions.ILike(n.ProtocoloAutorizacao, $"%{termo}%")));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IEnumerable<NfeDocumento>> ListarPendentesContingenciaAsync(int max = 100, CancellationToken ct = default)
    {
        return await db.NfeDocumentos
            .IgnoreQueryFilters()
            .Where(n => n.Status == StatusNfe.FalhaTransiente)
            .OrderBy(n => n.CriadoEm)
            .Take(max)
            .ToListAsync(ct);
    }

    public Task AddAsync(NfeDocumento nfe, CancellationToken ct = default)
    {
        db.NfeDocumentos.Add(nfe);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(NfeDocumento nfe, CancellationToken ct = default)
    {
        db.NfeDocumentos.Update(nfe);
        return Task.CompletedTask;
    }
}
