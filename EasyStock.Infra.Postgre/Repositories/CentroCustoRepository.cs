using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CentroCustoRepository(EasyStockDbContext db) : ICentroCustoRepository
{
    public Task AddAsync(CentroCusto centro, CancellationToken ct = default)
    {
        db.CentrosCusto.Add(centro);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CentroCusto centro, CancellationToken ct = default)
    {
        db.CentrosCusto.Update(centro);
        return Task.CompletedTask;
    }

    public Task<CentroCusto?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default)
        => db.CentrosCusto.FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public Task<CentroCusto?> GetByCodigoAsync(Guid empresaId, string codigo, CancellationToken ct = default)
        => db.CentrosCusto.FirstOrDefaultAsync(c =>
            c.EmpresaId == empresaId &&
            c.Codigo == codigo.Trim().ToUpper(), ct);

    public async Task<IReadOnlyList<CentroCusto>> ListarAsync(
        Guid empresaId,
        bool? ativo = null,
        Guid? lojaId = null,
        CancellationToken ct = default)
    {
        var q = db.CentrosCusto.AsNoTracking().Where(c => c.EmpresaId == empresaId);
        if (ativo.HasValue) q = q.Where(c => c.Ativo == ativo.Value);
        if (lojaId.HasValue) q = q.Where(c => c.LojaId == lojaId.Value);
        return await q.OrderBy(c => c.Codigo).ToListAsync(ct);
    }

    public async Task<bool> ExisteContaAbertaAsync(Guid empresaId, Guid centroCustoId, CancellationToken ct = default)
    {
        var temPagar = await db.ContasPagar.AsNoTracking()
            .AnyAsync(c =>
                c.EmpresaId == empresaId &&
                c.CentroCustoId == centroCustoId &&
                c.Status != StatusContaFinanceira.Cancelada &&
                c.Status != StatusContaFinanceira.Paga, ct);
        if (temPagar) return true;

        return await db.ContasReceber.AsNoTracking()
            .AnyAsync(c =>
                c.EmpresaId == empresaId &&
                c.CentroCustoId == centroCustoId &&
                c.Status != StatusContaFinanceira.Cancelada &&
                c.Status != StatusContaFinanceira.Paga, ct);
    }
}
