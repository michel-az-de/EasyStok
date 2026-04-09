namespace EasyStock.Web.Models.Api;

public record ProdutoResumo
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = string.Empty;
    public SkuBaseDto? SkuBase { get; init; }
    public Guid CategoriaId { get; init; }
    public DinheiroDto? PrecoReferencia { get; init; }
    public DinheiroDto? CustoReferencia { get; init; }
    public string? Marca { get; init; }
    public int Status { get; init; }

    public string StatusNome => Status == 0 ? "Ativo" : "Inativo";
}

public record SkuBaseDto
{
    public string Value { get; init; } = string.Empty;
}

