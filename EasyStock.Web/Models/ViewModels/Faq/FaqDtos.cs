namespace EasyStock.Web.Models.ViewModels.Faq;

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
    string Status,
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
    string Status,
    DateTime? PublicadoEm,
    DateTime AtualizadoEm,
    int Visualizacoes,
    int UtilCount,
    int NaoUtilCount);

public sealed record BuscarFaqResultadoDto(
    List<FaqItemListaDto> Itens,
    int Total,
    int Page,
    int PageSize);

public sealed class FaqIndexViewModel
{
    public string? Termo { get; set; }
    public Guid? CategoriaId { get; set; }
    public List<FaqCategoriaDto> Categorias { get; set; } = new();
    public BuscarFaqResultadoDto? Resultado { get; set; }
}

public sealed class FaqDetalheViewModel
{
    public FaqItemDetalheDto Item { get; set; } = null!;
    public List<FaqCategoriaDto> Categorias { get; set; } = new();
    public bool? FeedbackEnviado { get; set; }
}
