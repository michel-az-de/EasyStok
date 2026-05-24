namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o telefone informado não pode ser normalizado para E.164 BR
/// (+55 + DDD + 8/9 dígitos). Mapeada para HTTP 400 pelo global handler.
/// </summary>
public class TelefoneInvalidoException : RegraDeDominioVioladaException
{
    public TelefoneInvalidoException()
        : base("Telefone inválido. Use o formato +55 DDD número (ex: +5511997573992).")
    {
    }

    public TelefoneInvalidoException(string message)
        : base(message)
    {
    }

    public TelefoneInvalidoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
