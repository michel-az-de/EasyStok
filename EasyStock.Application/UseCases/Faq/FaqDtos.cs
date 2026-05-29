namespace EasyStock.Application.UseCases.Faq
{
    public sealed record FaqCategoriaDto(
        Guid Id,
        string Nome,
        string Slug,
        string? Descricao,
        string? Icone,
        int Ordem,
        bool Publica,
        int TotalItens);

    public sealed record FaqItemListaDto(
        Guid Id,
        Guid CategoriaId,
        string CategoriaNome,
        string CategoriaSlug,
        string Titulo,
        string Slug,
        string? Resumo,
        string[] Tags,
        FaqStatus Status,
        DateTime? PublicadoEm,
        int Visualizacoes,
        int UtilCount,
        int NaoUtilCount);

    public sealed record FaqItemDetalheDto(
        Guid Id,
        Guid CategoriaId,
        string CategoriaNome,
        string CategoriaSlug,
        string Titulo,
        string Slug,
        string Conteudo,
        string[] Tags,
        FaqStatus Status,
        DateTime? PublicadoEm,
        DateTime AtualizadoEm,
        int Visualizacoes,
        int UtilCount,
        int NaoUtilCount);

    public sealed record BuscarFaqResultado(
        IReadOnlyList<FaqItemListaDto> Itens,
        int Total,
        int Page,
        int PageSize);
}
