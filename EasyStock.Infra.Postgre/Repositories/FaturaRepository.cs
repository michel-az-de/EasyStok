using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class FaturaRepository(EasyStockDbContext db) : IFaturaRepository
{
    public Task AddAsync(Fatura fatura, CancellationToken ct = default) =>
        db.Faturas.AddAsync(fatura, ct).AsTask();

    public Task UpdateAsync(Fatura fatura, CancellationToken ct = default)
    {
        db.Faturas.Update(fatura);
        return Task.CompletedTask;
    }

    public Task<Fatura?> GetByIdAsync(Guid empresaId, Guid faturaId, CancellationToken ct = default) =>
        db.Faturas
            .Include(f => f.Itens.OrderBy(i => i.Ordem))
            .Include(f => f.Pagamentos)
            .Include(f => f.Eventos.OrderByDescending(e => e.OcorridoEm))
            .FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Id == faturaId, ct);

    public Task<Fatura?> GetByIdAdminAsync(Guid faturaId, CancellationToken ct = default) =>
        db.Faturas
            .Include(f => f.Empresa)
            .Include(f => f.Cliente)
            .Include(f => f.Itens.OrderBy(i => i.Ordem))
            .Include(f => f.Pagamentos)
            .Include(f => f.Eventos.OrderByDescending(e => e.OcorridoEm))
            .FirstOrDefaultAsync(f => f.Id == faturaId, ct);

    public async Task<(IReadOnlyList<Fatura> Itens, int Total)> ListarClienteAsync(
        Guid empresaId,
        StatusFatura? status = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = db.Faturas
            .AsNoTracking()
            .Where(f => f.EmpresaId == empresaId);

        q = AplicarFiltros(q, status, null, vencimentoDe, vencimentoAte, null, null, null);

        var total = await q.CountAsync(ct);

        var itens = await q
            .OrderByDescending(f => f.DataEmissao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (itens, total);
    }

    public async Task<(IReadOnlyList<Fatura> Itens, int Total)> ListarAdminAsync(
        Guid? empresaId = null,
        StatusFatura? status = null,
        OrigemFatura? origem = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        decimal? valorMin = null,
        decimal? valorMax = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken ct = default)
    {
        var q = db.Faturas
            .AsNoTracking()
            .Include(f => f.Empresa)
            .AsQueryable();

        if (empresaId.HasValue && empresaId.Value != Guid.Empty)
            q = q.Where(f => f.EmpresaId == empresaId.Value);

        q = AplicarFiltros(q, status, origem, vencimentoDe, vencimentoAte, valorMin, valorMax, busca);

        var total = await q.CountAsync(ct);

        var itens = await q
            .OrderByDescending(f => f.DataEmissao)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (itens, total);
    }

    public Task<Fatura?> GetByOrigemAsync(Guid empresaId, OrigemFatura origem, Guid origemRefId, CancellationToken ct = default) =>
        db.Faturas
            .FirstOrDefaultAsync(
                f => f.EmpresaId == empresaId
                  && f.Origem == origem
                  && f.OrigemRefId == origemRefId
                  && f.Status != StatusFatura.Cancelada,
                ct);

    private static IQueryable<Fatura> AplicarFiltros(
        IQueryable<Fatura> q,
        StatusFatura? status,
        OrigemFatura? origem,
        DateTime? vencimentoDe,
        DateTime? vencimentoAte,
        decimal? valorMin,
        decimal? valorMax,
        string? busca)
    {
        if (status.HasValue)
            q = q.Where(f => f.Status == status.Value);
        if (origem.HasValue)
            q = q.Where(f => f.Origem == origem.Value);
        if (vencimentoDe.HasValue)
            q = q.Where(f => f.DataVencimento >= vencimentoDe.Value);
        if (vencimentoAte.HasValue)
            q = q.Where(f => f.DataVencimento <= vencimentoAte.Value);
        if (valorMin.HasValue)
            q = q.Where(f => f.Total >= valorMin.Value);
        if (valorMax.HasValue)
            q = q.Where(f => f.Total <= valorMax.Value);
        if (!string.IsNullOrWhiteSpace(busca))
        {
            var b = busca.Trim();
            q = q.Where(f => f.Numero.Contains(b) || (f.Observacoes != null && f.Observacoes.Contains(b)));
        }
        return q;
    }
}
