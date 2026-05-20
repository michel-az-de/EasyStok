namespace EasyStock.Web.Models.Api;

// Mapeia o GerarPedidosDaListaResult do endpoint listas-compras/{id}/gerar-pedidos.
public record GerarPedidosResultApi
{
    public List<PedidoGeradoApi> Pedidos { get; init; } = new();
    public List<string> ItensSemFornecedor { get; init; } = new();
    public int ItensIgnorados { get; init; }
}

public record PedidoGeradoApi
{
    public Guid PedidoFornecedorId { get; init; }
    public Guid FornecedorId { get; init; }
    public string FornecedorNome { get; init; } = "";
    public string? FornecedorTelefone { get; init; }
    public decimal? ValorEstimado { get; init; }
    public List<PedidoGeradoItemApi> Itens { get; init; } = new();
}

public record PedidoGeradoItemApi
{
    public string Nome { get; init; } = "";
    public decimal Quantidade { get; init; }
    public string Unidade { get; init; } = "";
}
