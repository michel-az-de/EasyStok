namespace EasyStock.Domain.Entities;

public class ItemPedidoFornecedor
{
    public Guid Id { get; set; }
    public Guid PedidoFornecedorId { get; set; }
    public Guid EmpresaId { get; set; }
    public Guid? ProdutoId { get; set; }
    public Guid? ProdutoVariacaoId { get; set; }
    public string Descricao { get; set; } = null!;
    public decimal Quantidade { get; set; }
    public decimal? CustoUnitario { get; set; }
    public DateTime CriadoEm { get; set; }
    public DateTime AlteradoEm { get; set; }

    public PedidoFornecedor? Pedido { get; set; }

    public static ItemPedidoFornecedor Criar(
        Guid pedidoFornecedorId,
        Guid empresaId,
        Guid? produtoId,
        Guid? produtoVariacaoId,
        string descricao,
        decimal quantidade,
        decimal? custoUnitario)
    {
        var agora = DateTime.UtcNow;
        return new ItemPedidoFornecedor
        {
            Id = Guid.NewGuid(),
            PedidoFornecedorId = pedidoFornecedorId,
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            ProdutoVariacaoId = produtoVariacaoId,
            Descricao = descricao.Trim(),
            Quantidade = quantidade,
            CustoUnitario = custoUnitario,
            CriadoEm = agora,
            AlteradoEm = agora
        };
    }

    public void Atualizar(
        Guid? produtoId,
        Guid? produtoVariacaoId,
        string descricao,
        decimal quantidade,
        decimal? custoUnitario)
    {
        ProdutoId = produtoId;
        ProdutoVariacaoId = produtoVariacaoId;
        Descricao = descricao.Trim();
        Quantidade = quantidade;
        CustoUnitario = custoUnitario;
        AlteradoEm = DateTime.UtcNow;
    }
}
