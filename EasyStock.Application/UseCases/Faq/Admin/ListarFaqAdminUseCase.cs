namespace EasyStock.Application.UseCases.Faq.Admin
{
    public sealed record ListarFaqAdminQuery(
        FaqStatus? Status = null,
        Guid? CategoriaId = null,
        string? Busca = null,
        int Page = 1,
        int PageSize = 20);

    public sealed class ListarFaqAdminUseCase(IFaqAdminRepository repo)
    {
        public async Task<BuscarFaqResultado> ExecuteAsync(ListarFaqAdminQuery query, CancellationToken ct = default)
        {
            var (itens, total) = await repo.ListarItensAsync(query.Status, query.CategoriaId, query.Busca, query.Page, query.PageSize, ct);

            var dtos = itens.Select(i => new FaqItemListaDto(
                i.Id,
                i.CategoriaId,
                i.Categoria?.Nome ?? string.Empty,
                i.Categoria?.Slug ?? string.Empty,
                i.Titulo,
                i.Slug,
                i.ConteudoBusca.Length > 220 ? i.ConteudoBusca.Substring(0, 220) + "..." : i.ConteudoBusca,
                i.Tags,
                i.Status,
                i.PublicadoEm,
                i.Visualizacoes,
                i.UtilCount,
                i.NaoUtilCount)).ToList();

            return new BuscarFaqResultado(dtos, total, query.Page, query.PageSize);
        }
    }
}
