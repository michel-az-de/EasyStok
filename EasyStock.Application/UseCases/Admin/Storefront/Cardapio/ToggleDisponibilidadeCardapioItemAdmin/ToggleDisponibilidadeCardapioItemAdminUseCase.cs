using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Services.Campanhas;
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
    IUnitOfWork unitOfWork,
    AvisoItemComInteresse avisoInteresse)
    : IUseCase<ToggleDisponibilidadeCardapioItemAdminCommand, ToggleDisponibilidadeCardapioItemAdminResult>
{
    public async Task<ToggleDisponibilidadeCardapioItemAdminResult> ExecuteAsync(
        ToggleDisponibilidadeCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        var ofertadoAntes = AvisoItemComInteresse.Ofertado(item);
        if (item.Disponivel) item.MarcarEsgotado();
        else item.MarcarDisponivel();

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        // S31: depois do commit, avisa o console se o item voltou e alguém esperava por ele.
        await avisoInteresse.AvisarSeVoltouAsync(item, ofertadoAntes, command.EmpresaId);

        return new ToggleDisponibilidadeCardapioItemAdminResult(item.Id, item.Disponivel);
    }
}
