namespace EasyStock.Domain.Entities;

/// <summary>
/// Item de pedido de compra (fornecedor). Snapshot do produto+preço no
/// momento da compra. Quando o pedido é recebido, gera entrada de estoque
/// no <see cref="ItemEstoque"/> correspondente e <see cref="MovimentacaoEstoque"/>
/// de entrada.
/// </summary>
public class PedidoFornecedorItem
{
    public Guid Id { get; set; }
    public Guid PedidoFornecedorId { get; set; }

    /// <summary>
    /// Produto referenciado (opcional — pode ser produto novo ainda não
    /// cadastrado, nesse caso só Nome+Unidade são usados).
    /// </summary>
    public Guid? ProdutoId { get; set; }

    public string Nome { get; set; } = string.Empty;
    public string? Unidade { get; set; }

    public decimal Quantidade { get; set; }
    public decimal QuantidadeRecebida { get; set; }

    public decimal CustoUnitario { get; set; }
    public decimal Subtotal => Quantidade * CustoUnitario;

    public string? Observacao { get; set; }

    public DateTime CriadoEm { get; set; }

    public PedidoFornecedor? PedidoFornecedor { get; set; }
    public Produto? Produto { get; set; }
}
