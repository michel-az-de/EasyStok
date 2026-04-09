namespace EasyStock.Web.Models.Api;

public record CategoriaApi
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public Guid? CategoriaPaiId { get; init; }
}
