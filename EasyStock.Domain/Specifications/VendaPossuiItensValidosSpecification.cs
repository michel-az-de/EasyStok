namespace EasyStock.Domain.Specifications;

public class VendaPossuiItensValidosSpecification : IEspecificacao<Venda>
{
    public bool EhSatisfeitaPor(Venda venda)
    {
        if (venda.ItensVenda == null || !venda.ItensVenda.Any())
            return false;

        var produtoAtivoSpec = new ProdutoAtivoSpecification();

        return venda.ItensVenda.All(item =>
            item.Quantidade.Value > 0 &&
            item.Produto is not null &&
            produtoAtivoSpec.EhSatisfeitaPor(item.Produto));
    }
}
