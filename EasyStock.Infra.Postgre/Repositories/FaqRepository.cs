using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    /// <summary>
    /// Acesso publico ao FAQ. So devolve itens publicados de categorias publicas.
    /// </summary>
    public sealed class FaqRepository(EasyStockDbContext db) : IFaqRepository
    {
        public async Task<IReadOnlyList<FaqCategoria>> ListarCategoriasPublicasAsync(CancellationToken ct = default)
        {
            return await db.FaqCategorias
                .AsNoTracking()
                .Where(c => c.Publica)
                .OrderBy(c => c.Ordem)
                .ThenBy(c => c.Nome)
                .ToListAsync(ct);
        }

        public async Task<FaqItem?> ObterPorSlugAsync(string categoriaSlug, string itemSlug, CancellationToken ct = default)
        {
            var slugCat = (categoriaSlug ?? string.Empty).ToLowerInvariant();
            var slugItem = (itemSlug ?? string.Empty).ToLowerInvariant();

            return await db.FaqItens
                .AsNoTracking()
                .Include(i => i.Categoria)
                .Where(i => i.Status == FaqStatus.Publicado
                    && i.Categoria!.Publica
                    && i.Categoria.Slug == slugCat
                    && i.Slug == slugItem)
                .FirstOrDefaultAsync(ct);
        }

        public async Task<(IReadOnlyList<FaqItem> Itens, int Total)> BuscarAsync(
            string? termo,
            Guid? categoriaId,
            int page,
            int pageSize,
            CancellationToken ct = default)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);

            var query = db.FaqItens
                .AsNoTracking()
                .Include(i => i.Categoria)
                .Where(i => i.Status == FaqStatus.Publicado && i.Categoria!.Publica);

            if (categoriaId.HasValue && categoriaId.Value != Guid.Empty)
                query = query.Where(i => i.CategoriaId == categoriaId.Value);

            if (!string.IsNullOrWhiteSpace(termo))
            {
                var t = termo.Trim().ToLowerInvariant();
                // ILIKE simples — para FTS GIN usar Sql raw em milestone seguinte
                query = query.Where(i =>
                    EF.Functions.ILike(i.Titulo, $"%{t}%")
                    || EF.Functions.ILike(i.ConteudoBusca, $"%{t}%")
                    || EF.Functions.ILike(i.TagsCsv, $"%{t}%"));
            }

            var total = await query.CountAsync(ct);
            var itens = await query
                .OrderByDescending(i => i.Visualizacoes)
                .ThenByDescending(i => i.PublicadoEm)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);

            return (itens, total);
        }

        public async Task<IReadOnlyList<FaqItem>> ListarDestaquesAsync(int top, CancellationToken ct = default)
        {
            top = Math.Clamp(top, 1, 50);
            return await db.FaqItens
                .AsNoTracking()
                .Include(i => i.Categoria)
                .Where(i => i.Status == FaqStatus.Publicado && i.Categoria!.Publica)
                .OrderByDescending(i => i.Visualizacoes)
                .ThenByDescending(i => i.UtilCount)
                .Take(top)
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<FaqItem>> ListarPorCategoriaAsync(Guid categoriaId, CancellationToken ct = default)
        {
            return await db.FaqItens
                .AsNoTracking()
                .Where(i => i.CategoriaId == categoriaId && i.Status == FaqStatus.Publicado)
                .OrderBy(i => i.Ordem)
                .ThenBy(i => i.Titulo)
                .ToListAsync(ct);
        }

        public async Task RegistrarVisualizacaoAsync(FaqVisualizacao visualizacao, CancellationToken ct = default)
        {
            await db.FaqVisualizacoes.AddAsync(visualizacao, ct);
        }

        public async Task RegistrarFeedbackAsync(FaqFeedback feedback, CancellationToken ct = default)
        {
            await db.FaqFeedbacks.AddAsync(feedback, ct);
        }

        public async Task IncrementarContadoresAsync(Guid itemId, int deltaVisualizacao, int deltaUtil, int deltaNaoUtil, CancellationToken ct = default)
        {
            // update direto sem carregar a entidade — evita race em alto volume
            await db.FaqItens
                .Where(i => i.Id == itemId)
                .ExecuteUpdateAsync(u => u
                    .SetProperty(i => i.Visualizacoes, i => i.Visualizacoes + deltaVisualizacao)
                    .SetProperty(i => i.UtilCount, i => i.UtilCount + deltaUtil)
                    .SetProperty(i => i.NaoUtilCount, i => i.NaoUtilCount + deltaNaoUtil),
                    ct);
        }
    }
}
