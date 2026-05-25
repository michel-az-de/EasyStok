namespace EasyStock.Domain.Exceptions
{
    public class UsuarioNaoAutorizadoException : RegraDeDominioVioladaException
    {
        public UsuarioNaoAutorizadoException()
            : base("Usuario nao autorizado a realizar esta operacao.")
        {
        }

        public UsuarioNaoAutorizadoException(string message) : base(message) { }
    }
}
