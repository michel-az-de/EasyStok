using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Domain.Specifications;

public class ProdutoAtivoSpecification : IEspecificacao<Produto>
{
    public bool EhSatisfeitaPor(Produto produto)
    {
        return produto.Status == StatusProduto.Ativo;
    }
}
