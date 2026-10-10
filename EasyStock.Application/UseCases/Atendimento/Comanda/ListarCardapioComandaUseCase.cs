using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Menu;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Atendimento.Comanda;

/// <summary>Uma porção do prato na comanda (M1.4b, #1531): preço absoluto e se tem hoje.</summary>
public sealed record PorcaoDaComanda(Guid Id, string Rotulo, decimal Preco, string? Peso, bool Disponivel, bool Padrao);

/// <summary>
/// O menu público mais as porções de cada prato, por <c>cardapio_item_id</c>. As porções vão à parte
/// para o DTO público (e o ETag do site) não mudarem antes da Fase 2 do ADR-0035.
/// </summary>
public sealed record CardapioDaComandaResult(
    IReadOnlyList<CardapioItemPublicoDto> Itens, string TituloPublico, string Slug,
    IReadOnlyDictionary<Guid, IReadOnlyList<PorcaoDaComanda>> Porcoes);

/// <summary>
/// Cardápio da comanda do console (F03): o mesmo menu público da vitrine da empresa logada, que o agente
/// consulta (<c>consultar_cardapio</c>) e o site mostra. Os ids são os <c>cardapio_item_id</c> que o
/// pedido da conversa recebe. Sem vitrine ativa: <see cref="StorefrontNaoEncontradoException"/>.
/// M1.4b (#1531): traz também as porções, que só a comanda vê por enquanto.
/// </summary>
public sealed class ListarCardapioComandaUseCase(
    IStorefrontRepository storefrontRepository,
    ListarCardapioPublicoUseCase listarCardapio,
    ICardapioItemRepository cardapioRepository)
{
    public async Task<CardapioDaComandaResult> ExecuteAsync(Guid empresaId, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        var storefront = await storefrontRepository.GetByEmpresaAsync(empresaId, ct);
        if (storefront is null || !storefront.Ativo)
            throw new StorefrontNaoEncontradoException();
        var menu = await listarCardapio.ExecuteAsync(new ListarCardapioPublicoInput(storefront.Slug), ct);

        var noMenu = menu.Itens.Select(i => i.Id).ToHashSet();
        var porcoes = (await cardapioRepository.GetVisiveisDoStorefrontAsync(storefront.Id, ct))
            .Where(i => noMenu.Contains(i.Id) && i.TemVariacoes())
            .ToDictionary(i => i.Id, i => (IReadOnlyList<PorcaoDaComanda>)i.Variacoes
                .OrderBy(v => v.OrdemExibicao).ThenBy(v => v.CriadoEm).ThenBy(v => v.Id)
                .Select(v => new PorcaoDaComanda(v.Id, v.Rotulo, v.PrecoStorefront, v.PesoExibicao, v.Disponivel, v.EhPadrao))
                .ToList());
        return new CardapioDaComandaResult(menu.Itens, menu.TituloPublico, menu.Slug, porcoes);
    }
}
