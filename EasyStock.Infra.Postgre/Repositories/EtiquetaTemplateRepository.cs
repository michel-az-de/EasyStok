using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class EtiquetaTemplateRepository(EasyStockDbContext db) : IEtiquetaTemplateRepository
{
    public Task<IEnumerable<EtiquetaTemplateSistema>> ListSistemaAsync() =>
        Task.FromResult<IEnumerable<EtiquetaTemplateSistema>>(
            db.EtiquetaTemplatesSistema.AsNoTracking().OrderBy(t => t.Ordem).ToList());

    public Task<EtiquetaTemplateSistema?> GetSistemaByIdAsync(Guid id) =>
        db.EtiquetaTemplatesSistema.FirstOrDefaultAsync(t => t.Id == id);

    public Task<IEnumerable<EtiquetaTemplate>> ListEmpresaAsync(Guid empresaId) =>
        Task.FromResult<IEnumerable<EtiquetaTemplate>>(
            db.EtiquetaTemplates.AsNoTracking()
                .Where(t => t.EmpresaId == empresaId)
                .OrderByDescending(t => t.IsDefault)
                .ThenBy(t => t.Nome)
                .ToList());

    public Task<EtiquetaTemplate?> GetEmpresaByIdAsync(Guid empresaId, Guid id) =>
        db.EtiquetaTemplates.FirstOrDefaultAsync(t => t.EmpresaId == empresaId && t.Id == id);

    public Task AddEmpresaAsync(EtiquetaTemplate template)
    {
        db.EtiquetaTemplates.Add(template);
        return Task.CompletedTask;
    }

    public Task UpdateEmpresaAsync(EtiquetaTemplate template)
    {
        db.EtiquetaTemplates.Update(template);
        return Task.CompletedTask;
    }

    public Task RemoveEmpresaAsync(EtiquetaTemplate template)
    {
        db.EtiquetaTemplates.Remove(template);
        return Task.CompletedTask;
    }

    public Task<EtiquetaEmpresaDefault?> GetDefaultAsync(Guid empresaId) =>
        db.EtiquetaEmpresaDefaults.FirstOrDefaultAsync(d => d.EmpresaId == empresaId);

    public async Task UpsertDefaultAsync(EtiquetaEmpresaDefault defaultEntry)
    {
        var existing = await db.EtiquetaEmpresaDefaults
            .FirstOrDefaultAsync(d => d.EmpresaId == defaultEntry.EmpresaId);

        if (existing == null)
            db.EtiquetaEmpresaDefaults.Add(defaultEntry);
        else
        {
            existing.TemplateOrigem = defaultEntry.TemplateOrigem;
            existing.TemplateId    = defaultEntry.TemplateId;
            existing.AlteradoEm    = defaultEntry.AlteradoEm;
        }
    }

    public Task<int> CountSnapshotsByTemplateIdAsync(Guid templateId)
    {
        var idStr = templateId.ToString();
        return db.Set<LoteEtiqueta>()
            .Where(e => e.LayoutSnapshotMeta != null && e.LayoutSnapshotMeta.Contains(idStr))
            .CountAsync();
    }
}
