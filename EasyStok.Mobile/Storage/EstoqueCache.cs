using EasyStok.Mobile.Models;

namespace EasyStok.Mobile.Storage;

/// <summary>
/// Repositorio do cache local de itens de estoque. Faz upsert em batch
/// apos cada Pull do backend e retorna leitura rapida pra UI.
/// </summary>
public sealed class EstoqueCache : IEstoqueCache
{
    private readonly AppDatabase _db;

    public EstoqueCache(AppDatabase db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<CachedItemEstoque>> GetAllAsync(Guid empresaId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.Table<CachedItemEstoque>()
            .Where(x => x.EmpresaId == empresaId)
            .OrderBy(x => x.ProdutoNome)
            .ToListAsync();
    }

    public async Task UpsertManyAsync(IEnumerable<ItemEstoqueRemoto> items, Guid empresaId)
    {
        var conn = await _db.GetConnectionAsync();
        var now = DateTime.UtcNow;

        var rows = items.Select(d => new CachedItemEstoque
        {
            Id = d.Id,
            ProdutoId = d.ProdutoId,
            Sku = d.Sku,
            ProdutoNome = d.Produto?.Nome ?? d.Sku,
            Emoji = d.Produto?.Emoji,
            CategoriaId = d.Produto?.Categoria,
            Qty = d.Qty,
            Status = d.Status,
            LastMovUtc = d.LastMov.UtcDateTime,
            ValidadeUtc = d.Validade?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            Lote = d.Lote,
            CustoUnitario = d.CustoUnitario,
            PrecoVendaSugerido = d.PrecoVendaSugerido,
            EmpresaId = empresaId,
            CachedAtUtc = now,
        }).ToList();

        await conn.RunInTransactionAsync(c =>
        {
            foreach (var r in rows)
                c.InsertOrReplace(r);
        });
    }

    public async Task<int> CountAsync(Guid empresaId)
    {
        var conn = await _db.GetConnectionAsync();
        return await conn.Table<CachedItemEstoque>()
            .Where(x => x.EmpresaId == empresaId)
            .CountAsync();
    }
}

public interface IEstoqueCache
{
    Task<IReadOnlyList<CachedItemEstoque>> GetAllAsync(Guid empresaId);
    Task UpsertManyAsync(IEnumerable<ItemEstoqueRemoto> items, Guid empresaId);
    Task<int> CountAsync(Guid empresaId);
}
