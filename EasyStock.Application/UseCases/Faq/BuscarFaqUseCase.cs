using EasyStock.Application.Ports.Output.Persistence;

namespace EasyStock.Application.UseCases.Faq
{
    public sealed record BuscarFaqQuery(
        string? Termo = null,
        Guid? CategoriaId = null,
        int Page = 1,
        int PageSize = 10);

    /// <summary>
    /// Pesquisa itens de FAQ publicados. Suporta texto livre (FTS Postgres) e
    /// filtro por categoria. Paginacao com pageSize clampado a 50.
    /// Base global: sem filtro de tenant.
    /// </summary>
    public sealed class BuscarFaqUseCase(IFaqRepository faqRepo)
    {
        public async Task<BuscarFaqResultado> ExecuteAsync(BuscarFaqQuery query, CancellationToken ct = default)
        {
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Clamp(query.PageSize, 1, 50);

            var (itens, total) = await faqRepo.BuscarAsync(query.Termo, query.CategoriaId, page, pageSize, ct);

            var dtos = itens
                .Select(i => new FaqItemListaDto(
                    i.Id,
                    i.CategoriaId,
                    i.Categoria?.Nome ?? string.Empty,
                    i.Categoria?.Slug ?? string.Empty,
                    i.Titulo,
                    i.Slug,
                    Resumir(i.ConteudoBusca, 220),
                    i.Tags,
                    i.Status,
                    i.PublicadoEm,
                    i.Visualizacoes,
                    i.UtilCount,
                    i.NaoUtilCount))
                .ToList();

            return new BuscarFaqResultado(dtos, total, page, pageSize);
        }

        private static string? Resumir(string? texto, int max)
        {
            if (string.IsNullOrWhiteSpace(texto)) return null;
            if (texto.Length <= max) return texto;
            return texto.Substring(0, max).TrimEnd() + "...";
        }
    }
}
