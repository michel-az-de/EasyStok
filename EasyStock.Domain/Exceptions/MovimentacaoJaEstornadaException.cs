using System;

namespace EasyStock.Domain.Exceptions
{
    public class MovimentacaoJaEstornadaException(Guid movimentacaoId) : RegraDeDominioVioladaException($"A movimentacao '{movimentacaoId}' ja foi estornada anteriormente.")
    {
    }
}
