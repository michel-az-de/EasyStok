using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CategoriaFinanceiraRepository(EasyStockDbContext db) : ICategoriaFinanceiraRepository
{
    public Task AddAsync(CategoriaFinanceira categoria, CancellationToken ct = default)
    {
        db.CategoriasFinanceiras.Add(categoria);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CategoriaFinanceira categoria, CancellationToken ct = default)
    {
        db.CategoriasFinanceiras.Update(categoria);
        return Task.CompletedTask;
    }

    public Task<CategoriaFinanceira?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default)
        => db.CategoriasFinanceiras
            .Include(c => c.Parent)
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Id == id, ct);

    public async Task<IReadOnlyList<CategoriaFinanceira>> ListarAsync(
        Guid empresaId,
        bool? ativa = null,
        TipoCategoriaFinanceira? tipo = null,
        CancellationToken ct = default)
    {
        var q = db.CategoriasFinanceiras.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId);
        if (ativa.HasValue) q = q.Where(c => c.Ativa == ativa.Value);
        if (tipo.HasValue) q = q.Where(c => c.Tipo == tipo.Value || c.Tipo == TipoCategoriaFinanceira.Ambas);
        return await q.OrderBy(c => c.Profundidade).ThenBy(c => c.Ordem).ThenBy(c => c.Nome).ToListAsync(ct);
    }

    public Task<bool> ExisteNomeAsync(Guid empresaId, string nome, Guid? parentId, Guid? excludeId = null, CancellationToken ct = default)
    {
        var q = db.CategoriasFinanceiras.AsNoTracking()
            .Where(c =>
                c.EmpresaId == empresaId &&
                c.Ativa &&
                c.ParentId == parentId &&
                c.Nome.ToLower() == nome.Trim().ToLower());
        if (excludeId.HasValue) q = q.Where(c => c.Id != excludeId.Value);
        return q.AnyAsync(ct);
    }

    public Task<bool> ExisteContaAbertaAsync(Guid empresaId, Guid categoriaId, CancellationToken ct = default)
    {
        var pagar = db.ContasPagar.AsNoTracking()
            .AnyAsync(c =>
                c.EmpresaId == empresaId &&
                c.CategoriaFinanceiraId == categoriaId &&
                c.Status != StatusContaFinanceira.Cancelada &&
                c.Status != StatusContaFinanceira.Paga, ct);
        var receber = db.ContasReceber.AsNoTracking()
            .AnyAsync(c =>
                c.EmpresaId == empresaId &&
                c.CategoriaFinanceiraId == categoriaId &&
                c.Status != StatusContaFinanceira.Cancelada &&
                c.Status != StatusContaFinanceira.Paga, ct);
        return Task.WhenAll(pagar, receber).ContinueWith(_ => pagar.Result || receber.Result, ct);
    }
}
