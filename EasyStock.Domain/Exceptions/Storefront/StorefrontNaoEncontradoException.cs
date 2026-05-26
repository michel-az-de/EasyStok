namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando um Storefront não é encontrado — seja por slug (resolução
/// pública), por id (admin) ou por empresa. Mapeada para HTTP 404 pelo handler.
/// </summary>
public class StorefrontNaoEncontradoException : RegraDeDominioVioladaException
{
    public StorefrontNaoEncontradoException()
        : base("Storefront não encontrado.")
    {
    }

    public StorefrontNaoEncontradoException(string slug)
        : base($"Storefront '{slug}' não encontrado.")
    {
    }

    public StorefrontNaoEncontradoException(Guid storefrontId)
        : base($"Storefront {storefrontId} não encontrado.")
    {
    }

    public StorefrontNaoEncontradoException(string slug, Exception innerException)
        : base($"Storefront '{slug}' não encontrado.", innerException)
    {
    }
}
