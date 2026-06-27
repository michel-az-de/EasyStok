using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Storefront;

public sealed class CardapioItemRepository(EasyStockDbContext db) : ICardapioItemRepository
{
    public Task<CardapioItem?> GetByIdAsync(Guid storefrontId, Guid id, CancellationToken ct = default) =>
        db.CardapioItens
            .Include(c => c.Produto)
            .FirstOrDefaultAsync(c => c.StorefrontId == storefrontId && c.Id == id, ct);

    public Task<CardapioItem?> GetByIdAndScopeAsync(Guid storefrontId, Guid itemId, Guid? empresaId, CancellationToken ct = default)
    {
        // ADR-0035: admin (Obter/Editar) precisa enxergar as opções e a seção do item.
        var query = db.CardapioItens
            .AsSplitQuery()
            .Include(c => c.Produto)
            .Include(c => c.Variacoes)
            .Include(c => c.Secao)
            .Where(c => c.StorefrontId == storefrontId && c.Id == itemId);

        // Escopo de tenant: só itens cujo Storefront pertence à empresa. SuperAdmin (null) ignora.
        // CardapioItem não tem EmpresaId (logo não recebe o global query filter de tenant) nem
        // navegação para Storefront — por isso o EXISTS em db.Storefronts. O `s.EmpresaId == emp`
        // é REDUNDANTE com o global query filter que já incide sobre db.Storefronts (s.EmpresaId ==
        // CurrentTenantId), mas é mantido de propósito como defense-in-depth: o escopo do item passa
        // a ser garantido aqui de forma explícita, independente do contexto de tenant do request
        // (ADR-0031 §3; auditoria 2026-06-11, finding p3 — manter ambos é intencional).
        if (empresaId.HasValue)
        {
            var emp = empresaId.Value;
            query = query.Where(c => db.Storefronts.Any(s => s.Id == c.StorefrontId && s.EmpresaId == emp));
        }

        return query.FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<CardapioItem>> GetVisiveisDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        // ADR-0035: carrega as opções (item guarda-chuva) e a seção + ancestrais (até 3 níveis,
        // CHECK nivel<=2) para o categoriaPath. AsSplitQuery evita produto cartesiano com a
        // coleção Variacoes. A ordenação determinística final (ETag estável) é feita in-memory
        // no ListarCardapioPublicoUseCase — o OrderBy aqui é só pré-ordenação SQL.
        await db.CardapioItens
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Produto)
                .ThenInclude(p => p!.Categoria)
            .Include(c => c.Variacoes)
            .Include(c => c.Secao)
                .ThenInclude(s => s!.SecaoPai)
                    .ThenInclude(s => s!.SecaoPai)
            .Where(c => c.StorefrontId == storefrontId && c.Visivel)
            .OrderBy(c => c.Produto!.Categoria!.Nome)
            .ThenBy(c => c.OrdemExibicao)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CardapioItem>> GetTodosDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        // ADR-0035: inclui opções (p/ badge "N opções · a partir de") e seção. AsSplitQuery evita
        // produto cartesiano com a coleção Variacoes.
        await db.CardapioItens
            .AsNoTracking()
            .AsSplitQuery()
            .Include(c => c.Produto)
            .Include(c => c.Variacoes)
            .Include(c => c.Secao)
            .Where(c => c.StorefrontId == storefrontId)
            .OrderBy(c => c.OrdemExibicao)
            .ToListAsync(ct);

    public Task AddAsync(CardapioItem item, CancellationToken ct = default)
    {
        db.CardapioItens.Add(item);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CardapioItem item, CancellationToken ct = default)
    {
        // Item carregado TRACKED por GetByIdAndScopeAsync (com Include Variacoes): o change tracker
        // ja conhece o grafo — opcoes novas em Added, alteradas em Modified, removidas em Deleted — e
        // o CommitAsync (SaveChanges) persiste tudo. NAO chamar db.Update num agregado tracked: ele
        // forca o grafo inteiro para Modified e, como a opcao nova nasce com Guid client-gen
        // (CardapioItemVariacao.Criar -> Guid.NewGuid), vira UPDATE de linha inexistente ->
        // DbUpdateConcurrencyException -> 500 ao adicionar opcao na edicao (ADR-0035 / 434d23fc).
        // So usamos Update se a entidade vier destacada (caller que carregou sem tracking).
        if (db.Entry(item).State == Microsoft.EntityFrameworkCore.EntityState.Detached)
            db.CardapioItens.Update(item);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(CardapioItem item, CancellationToken ct = default)
    {
        db.CardapioItens.Remove(item);
        return Task.CompletedTask;
    }

    public Task<int> ContarPorStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        db.CardapioItens.CountAsync(c => c.StorefrontId == storefrontId, ct);

    // Mesmo escopo do ContarPorStorefrontAsync (mesmo db, sem IgnoreQueryFilters; CardapioItem
    // não tem EmpresaId logo não recebe filtro de tenant — o escopo é puramente por StorefrontId).
    // 1 query GROUP BY no lugar de N COUNTs. Storefronts sem item somem do resultado (default 0).
    public async Task<IReadOnlyDictionary<Guid, int>> ContarPorStorefrontsAsync(
        IReadOnlyCollection<Guid> storefrontIds, CancellationToken ct = default)
    {
        if (storefrontIds.Count == 0) return new Dictionary<Guid, int>();

        return await db.CardapioItens
            .Where(c => storefrontIds.Contains(c.StorefrontId))
            .GroupBy(c => c.StorefrontId)
            .Select(g => new { StorefrontId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.StorefrontId, x => x.Count, ct);
    }

    public async Task<IReadOnlyCollection<Guid>> GetProdutoIdsDoStorefrontAsync(Guid storefrontId, CancellationToken ct = default) =>
        await db.CardapioItens
            .AsNoTracking()
            .Where(c => c.StorefrontId == storefrontId && c.ProdutoId.HasValue)
            .Select(c => c.ProdutoId!.Value)   // Guid? → Guid: safe após filtro HasValue
            .ToListAsync(ct);
}
