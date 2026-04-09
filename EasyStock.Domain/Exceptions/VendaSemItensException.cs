using System;

namespace EasyStock.Domain.Exceptions
{
    public class VendaSemItensException(Guid vendaId) : RegraDeDominioVioladaException($"Não é possível concluir a venda '{vendaId}': venda sem itens.")
    {
    }
}
