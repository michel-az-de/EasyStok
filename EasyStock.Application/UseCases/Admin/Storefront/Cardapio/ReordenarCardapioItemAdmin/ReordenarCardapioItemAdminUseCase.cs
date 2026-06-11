using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.ReordenarCardapioItemAdmin;

/// <summary>
/// Atualiza apenas a OrdemExibicao de um item. Aceita double — convenção do
/// projeto permite "1.5" entre "1.0" e "2.0" para reordenação cheap (ver
/// CardapioItem.OrdemExibicao XmlDoc).
/// </summary>
public sealed record ReordenarCardapioItemAdminCommand(
    Guid StorefrontId,
    Guid ItemId,
    double NovaOrdem,
    Guid? EmpresaId = null) : ICommand;

public sealed record ReordenarCardapioItemAdminResult(Guid ItemId, double Ordem);

public class ReordenarCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IUnitOfWork unitOfWork)
    : IUseCase<ReordenarCardapioItemAdminCommand, ReordenarCardapioItemAdminResult>
{
    public async Task<ReordenarCardapioItemAdminResult> ExecuteAsync(ReordenarCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        item.DefinirOrdem(command.NovaOrdem);

        await cardapioRepository.UpdateAsync(item);
        await unitOfWork.CommitAsync();

        return new ReordenarCardapioItemAdminResult(item.Id, item.OrdemExibicao);
    }
}
