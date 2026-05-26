namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando se tenta criar um Storefront com slug já em uso por outro
/// tenant. Slug é único globalmente (PII na URL pública — ver Storefront.cs).
/// Mapeada para HTTP 422 no controller admin.
/// </summary>
public class StorefrontSlugDuplicadoException : RegraDeDominioVioladaException
{
    public string Slug { get; }

    public StorefrontSlugDuplicadoException(string slug)
        : base($"Slug '{slug}' já está em uso por outro storefront.")
    {
        Slug = slug;
    }
}
