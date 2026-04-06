namespace EasyStock.Web.Models.Api;

public record PedidoFornecedor
{
    public required string Id { get; init; }
    public required string FornId { get; init; }
    public DateOnly Data { get; init; }
    public DateOnly? Previsao { get; init; }
    public DateOnly? DtRecebimento { get; init; }
    public required string Canal { get; init; }
    public string? Tracking { get; init; }
    public decimal Valor { get; init; }
    public required string Status { get; init; }
    public string? Obs { get; init; }
    public List<PedidoItem> Itens { get; init; } = [];
    public Fornecedor? Fornecedor { get; init; }
}

public record PedidoItem(string? ProdutoId, string Nome, int Qty, decimal Custo);
