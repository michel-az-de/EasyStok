using System;

namespace EasyStock.Domain.Exceptions
{
    public class RegraDeDominioVioladaException : Exception
    {
        public RegraDeDominioVioladaException()
        : base("Violação de regra de domínio.")
        {
        }

        public RegraDeDominioVioladaException(string message)
        : base(message)
        {
        }

        public RegraDeDominioVioladaException(string message, Exception innerException)
        : base(message, innerException)
        {
        }
    }
}
