using EasyStock.Application.Ports.Output.Persistence.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio;

/// <summary>
/// #2037: a seção do prato tem de ser da mesma vitrine, como no console (<c>ItensDoCardapioComandaUseCase</c>).
/// <c>cardapio_secoes</c> não tem <c>EmpresaId</c> (sem filtro do EF nem RLS): sem conferir, o prato de uma empresa
/// apontava para a seção de outra e herdava o "oculta" e a ordem dela.
/// </summary>
internal static class SecaoDaVitrine
{
    public static async Task ExigirAsync(ICardapioSecaoRepository secoes, Guid storefrontId, Guid secaoId) =>
        _ = await secoes.GetByIdAsync(storefrontId, secaoId)
            ?? throw new UseCaseValidationException("Categoria não encontrada.");
}
