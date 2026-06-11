using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ToggleDisponibilidadeCardapioItemAdmin;

/// <summary>
/// Alterna Disponivel (esgotado/disponível) do CardapioItem.
/// Idempotente — chamar 2x volta ao estado original.
/// </summary>
public sealed record ToggleDisponibilidadeCardapioItemAdminCommand(Guid StorefrontId, Guid ItemId, Guid? EmpresaId = null) : ICommand;

public sealed record ToggleDisponibilidadeCardapioItemAdminResult(Guid ItemId, bool DisponivelAgora);

public class ToggleDisponibilidadeCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<ToggleDisponibilidadeCardapioItemAdminCommand, ToggleDisponibilidadeCardapioItemAdminResult>
{
    public async Task<ToggleDisponibilidadeCardapioItemAdminResult> ExecuteAsync(
        ToggleDisponibilidadeCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        if (item.Disponivel) item.MarcarEsgotado();
        else item.MarcarDisponivel();

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        return new ToggleDisponibilidadeCardapioItemAdminResult(item.Id, item.Disponivel);
    }
}
