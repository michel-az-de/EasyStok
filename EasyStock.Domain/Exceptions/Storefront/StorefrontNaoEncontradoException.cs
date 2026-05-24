namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando um slug de storefront não resolve para nenhum tenant — ou
/// resolve mas o storefront está inativo. Mapeada para HTTP 404 pelo handler.
/// </summary>
public class StorefrontNaoEncontradoException : RegraDeDominioVioladaException
{
    public StorefrontNaoEncontradoException(string slug)
        : base($"Storefront '{slug}' não encontrado.")
    {
    }

    public StorefrontNaoEncontradoException(string slug, Exception innerException)
        : base($"Storefront '{slug}' não encontrado.", innerException)
    {
    }
}
