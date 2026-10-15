using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class ExpedienteLojaRepository(EasyStockDbContext db) : IExpedienteLojaRepository
{
    public Task<ExpedienteLoja?> GetByEmpresaIdAsync(Guid empresaId, CancellationToken ct = default) =>
        db.ExpedientesLoja.FirstOrDefaultAsync(x => x.EmpresaId == empresaId, ct);

    // Mesmo acesso do StorefrontRepository.GetBySlugAsync: o checkout público não tem JWT.
    public Task<ExpedienteLoja?> GetPublicoAsync(Guid empresaId, CancellationToken ct = default) =>
        db.ExpedientesLoja.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.EmpresaId == empresaId, ct);

    public Task AddAsync(ExpedienteLoja expediente, CancellationToken ct = default) =>
        db.ExpedientesLoja.AddAsync(expediente, ct).AsTask();

    public Task UpdateAsync(ExpedienteLoja expediente, CancellationToken ct = default)
    {
        if (db.Entry(expediente).State == EntityState.Detached)
            db.ExpedientesLoja.Update(expediente);
        return Task.CompletedTask;
    }
}
