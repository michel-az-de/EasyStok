using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.Ports.Output.Persistence.Campanhas;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Entities.Storefront;

namespace EasyStock.Application.Services.Campanhas;

/// <summary>
/// S31: quando um item passa a ser oferecido de novo (visível e disponível) e há interesses abertos,
/// avisa o console por SSE (<see cref="EventosOperacao.CardapioItemComInteresse"/>). Só sugere: nada
/// vai ao cliente sem ação da dona (D8). Chamar depois do commit.
/// </summary>
public sealed class AvisoItemComInteresse(
    IStorefrontRepository storefronts,
    IInteresseItemRepository interesses,
    IOperacaoEventPublisher publisher)
{
    public static bool Ofertado(CardapioItem item) => item.Visivel && item.Disponivel;

    /// <param name="ofertadoAntes">Se o item já estava visível e disponível antes da alteração.</param>
    /// <param name="empresaId">Escopo do chamador; nulo (SuperAdmin) resolve pela loja do item.</param>
    public async Task AvisarSeVoltouAsync(CardapioItem item, bool ofertadoAntes, Guid? empresaId, CancellationToken ct = default)
    {
        if (ofertadoAntes || !Ofertado(item)) return;

        var empresa = empresaId ?? (await storefronts.GetByIdAsync(item.StorefrontId, ct))?.EmpresaId;
        if (empresa is not { } id) return;

        var quantidade = await interesses.ContarAbertosDoItemAsync(id, item.Id, ct);
        if (quantidade == 0) return;

        await publisher.PublicarAsync(
            EventosOperacao.CardapioItemComInteresse, id, new CardapioItemComInteresseOperacao(item.Id, quantidade), ct);
    }
}
