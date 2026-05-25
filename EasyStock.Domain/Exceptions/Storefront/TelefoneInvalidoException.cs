namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o telefone não está no formato E.164 BR após normalização.
/// Mapeada para HTTP 400 pelo handler.
/// </summary>
public class TelefoneInvalidoException : RegraDeDominioVioladaException
{
    public TelefoneInvalidoException()
        : base("Telefone inválido. Informe um número celular brasileiro (DDD + número).")
    {
    }

    public TelefoneInvalidoException(string message)
        : base(message)
    {
    }
}
