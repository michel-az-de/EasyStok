namespace EasyStock.Domain.Exceptions
{
    public class ProdutoInativoException(Guid produtoId) : RegraDeDominioVioladaException($"Operação inválida: o produto '{produtoId}' está inativo.")
    {
    }
}
