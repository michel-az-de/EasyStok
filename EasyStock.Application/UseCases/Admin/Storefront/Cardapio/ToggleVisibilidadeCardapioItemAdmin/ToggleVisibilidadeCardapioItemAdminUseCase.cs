using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;

/// <summary>
/// Alterna Visivel do CardapioItem. Operação idempotente — chamar 2x volta ao
/// estado original (toggle).
/// </summary>
public sealed record ToggleVisibilidadeCardapioItemAdminCommand(Guid StorefrontId, Guid ItemId, Guid? EmpresaId = null) : ICommand;

public sealed record ToggleVisibilidadeCardapioItemAdminResult(Guid ItemId, bool VisivelAgora);

public class ToggleVisibilidadeCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork,
    AvisoItemComInteresse avisoInteresse)
    : IUseCase<ToggleVisibilidadeCardapioItemAdminCommand, ToggleVisibilidadeCardapioItemAdminResult>
{
    public async Task<ToggleVisibilidadeCardapioItemAdminResult> ExecuteAsync(
        ToggleVisibilidadeCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        var ofertadoAntes = AvisoItemComInteresse.Ofertado(item);
        if (item.Visivel) item.Ocultar();
        else item.TornarVisivel();

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        // S31: depois do commit, avisa o console se o item voltou e alguém esperava por ele.
        await avisoInteresse.AvisarSeVoltouAsync(item, ofertadoAntes, command.EmpresaId);

        return new ToggleVisibilidadeCardapioItemAdminResult(item.Id, item.Visivel);
    }
}
