using System;

namespace EasyStock.Domain.Exceptions
{
    public class ItemEstoqueVencidoException(Guid itemEstoqueId, DateTime dataValidade) : RegraDeDominioVioladaException($"Operação inválida: item de estoque '{itemEstoqueId}' está vencido em {dataValidade:yyyy-MM-dd}.")
    {
    }
}
