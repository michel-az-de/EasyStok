namespace EasyStock.Web.Models.Api;

public record PlanoDto
{
    public Guid Id { get; init; }
    public string Nome { get; init; } = "";
    public string? Descricao { get; init; }
    public int LimiteLojas { get; init; }
    public int LimiteUsuarios { get; init; }
    public int LimiteProdutos { get; init; }
    public decimal PrecoMensal { get; init; }
}
