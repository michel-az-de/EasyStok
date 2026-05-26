namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o CEP informado não pode ser normalizado para 8 dígitos
/// (vazio, com letras, comprimento errado). Mapeada para HTTP 400.
/// </summary>
public class CepInvalidoException : RegraDeDominioVioladaException
{
    public CepInvalidoException()
        : base("CEP inválido. Informe 8 dígitos (ex: 05500-000 ou 05500000).")
    {
    }

    public CepInvalidoException(string message)
        : base(message)
    {
    }

    public CepInvalidoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
