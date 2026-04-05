namespace EasyStock.Web.Models.Api;

public record Fornecedor
{
    public required string Id { get; init; }
    public required string Nome { get; init; }
    public string? Cnpj { get; init; }
    public string? Resp { get; init; }
    public string? Email { get; init; }
    public string? Tel { get; init; }
    public int Lead { get; init; }
    public required string Pgto { get; init; }
    public required string Tipo { get; init; }
    public required string Cats { get; init; }
    public string? Site { get; init; }
    public string? Min { get; init; }
    public string? Frete { get; init; }
    public string? Obs { get; init; }
    public required string Status { get; init; }
}
