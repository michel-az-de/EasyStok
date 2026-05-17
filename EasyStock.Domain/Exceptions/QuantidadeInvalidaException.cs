using System;

namespace EasyStock.Domain.Exceptions
{
    public class QuantidadeInvalidaException : RegraDeDominioVioladaException
    {
        public QuantidadeInvalidaException(decimal quantidade)
        : base($"Quantidade inválida: {quantidade}.")
        {
        }

        public QuantidadeInvalidaException(string message)
        : base(message)
        {
        }
    }
}
