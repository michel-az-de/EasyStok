using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <summary>
/// Cardápio da comanda do console (F03): o mesmo menu público da vitrine da empresa logada, que o agente
/// consulta (<c>consultar_cardapio</c>) e o site mostra. Os ids são os <c>cardapio_item_id</c> que o
/// pedido da conversa recebe. Sem vitrine ativa: <see cref="StorefrontNaoEncontradoException"/>.
/// </summary>
public sealed class ListarCardapioComandaUseCase(
    IStorefrontRepository storefrontRepository,
    ListarCardapioPublicoUseCase listarCardapio)
{
    public async Task<ListarCardapioPublicoResult> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        return await listarCardapio.ExecuteAsync(new ListarCardapioPublicoInput(storefront.Slug), ct);
    }
}
