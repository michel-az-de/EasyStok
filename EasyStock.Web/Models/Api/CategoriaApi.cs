namespace EasyStock.Web.Models.Api;

public record CategoriaApi
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public Guid? CategoriaPaiId { get; init; }
    public int? QuantidadeMinima { get; init; }
    public int? QuantidadeCritica { get; init; }
}
