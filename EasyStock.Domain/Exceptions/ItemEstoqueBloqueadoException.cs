using System;

namespace EasyStock.Domain.Exceptions
{
    public class ItemEstoqueBloqueadoException(Guid itemEstoqueId) : RegraDeDominioVioladaException($"Operação inválida: item de estoque '{itemEstoqueId}' está bloqueado.")
    {
    }
}
