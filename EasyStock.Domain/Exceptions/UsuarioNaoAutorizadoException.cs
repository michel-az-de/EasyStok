namespace EasyStock.Domain.Exceptions
{
    public class UsuarioNaoAutorizadoException() : RegraDeDominioVioladaException("Usuario nao autorizado a realizar esta operacao.")
    {
        public UsuarioNaoAutorizadoException(string message) : this()
        {
        }
    }
}
