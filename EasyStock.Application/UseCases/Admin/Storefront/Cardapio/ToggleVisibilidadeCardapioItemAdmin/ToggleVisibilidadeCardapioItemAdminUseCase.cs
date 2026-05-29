using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleVisibilidadeCardapioItemAdmin;

/// <summary>
/// Alterna Visivel do CardapioItem. Operação idempotente — chamar 2x volta ao
/// estado original (toggle).
/// </summary>
public sealed record ToggleVisibilidadeCardapioItemAdminCommand(Guid StorefrontId, Guid ItemId) : ICommand;

public sealed record ToggleVisibilidadeCardapioItemAdminResult(Guid ItemId, bool VisivelAgora);

public class ToggleVisibilidadeCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<ToggleVisibilidadeCardapioItemAdminCommand, ToggleVisibilidadeCardapioItemAdminResult>
{
    public async Task<ToggleVisibilidadeCardapioItemAdminResult> ExecuteAsync(
        ToggleVisibilidadeCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAsync(command.StorefrontId, command.ItemId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        if (item.Visivel) item.Ocultar();
        else item.TornarVisivel();

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        return new ToggleVisibilidadeCardapioItemAdminResult(item.Id, item.Visivel);
    }
}
