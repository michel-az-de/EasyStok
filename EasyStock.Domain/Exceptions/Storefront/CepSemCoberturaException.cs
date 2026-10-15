namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando o CEP é válido mas nenhuma <c>FreteZona</c> ativa do
/// storefront o cobre. Mapeada para HTTP 422 (semanticamente: input está
/// bem-formado, mas a entrega precisa de combinação humana).
/// </summary>
public class CepSemCoberturaException : RegraDeDominioVioladaException
{
    public CepSemCoberturaException()
        : base("Vamos combinar a entrega e o frete com a equipe.")
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
