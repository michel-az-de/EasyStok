namespace EasyStock.Domain.Exceptions
{
    public class CredenciaisInvalidasException : RegraDeDominioVioladaException
    {
        public CredenciaisInvalidasException() : base("Credenciais invalidas.") { }
        public CredenciaisInvalidasException(string message) : base(message) { }
    }
}
