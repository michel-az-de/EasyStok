namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o CEP é válido mas nenhuma <c>FreteZona</c> ativa do
/// storefront o cobre. Mapeada para HTTP 422 (semanticamente: input está
/// bem-formado, mas a regra de negócio rejeita).
/// </summary>
public class CepSemCoberturaException : RegraDeDominioVioladaException
{
    public CepSemCoberturaException()
        : base("Não entregamos neste CEP.")
    {
    }

    public CepSemCoberturaException(string message)
        : base(message)
    {
    }

    public CepSemCoberturaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
