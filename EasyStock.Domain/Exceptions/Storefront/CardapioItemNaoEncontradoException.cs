namespace EasyStock.Domain.Exceptions.Storefront;

/// <summary>
/// Lançada quando um CardapioItem solicitado por id não existe no storefront
/// indicado. Mapeada para HTTP 404 nos endpoints admin.
/// </summary>
public class CardapioItemNaoEncontradoException : RegraDeDominioVioladaException
{
    public Guid StorefrontId { get; }
    public Guid ItemId { get; }

    public CardapioItemNaoEncontradoException(Guid storefrontId, Guid itemId)
        : base($"Item de cardápio {itemId} não encontrado no storefront {storefrontId}.")
    {
        StorefrontId = storefrontId;
        ItemId = itemId;
    }
}
