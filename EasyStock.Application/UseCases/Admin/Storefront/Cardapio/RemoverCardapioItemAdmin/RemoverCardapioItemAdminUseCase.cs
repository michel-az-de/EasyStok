using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.Ports.Output.Storage;
using EasyStock.Application.UseCases.GerenciarUploads;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Application.UseCases.Admin.Storefront.Cardapio.RemoverCardapioItemAdmin;

// EmpresaId: null = SuperAdmin; com valor = escopo do tenant (item de outra empresa → 404).
public sealed record RemoverCardapioItemAdminCommand(Guid StorefrontId, Guid ItemId, Guid? EmpresaId = null) : ICommand;

public sealed record RemoverCardapioItemAdminResult(Guid ItemId);

/// <summary>
/// Remove (hard delete) um item do cardápio. Seguro: não há FK de pedido/checkout
/// para cardapio_item (a linha do pedido é snapshot), e remover libera o nome único
/// para readição (copy-deck: "você pode adicionar de novo depois"). A foto vai junto
/// num delete best-effort no storage (job de GC limpa qualquer órfão).
/// </summary>
public class RemoverCardapioItemAdminUseCase(
    ICardapioItemRepository cardapioRepository,
    IFileStorage fileStorage,
    IUnitOfWork unitOfWork)
    : IUseCase<RemoverCardapioItemAdminCommand, RemoverCardapioItemAdminResult>
{
    public async Task<RemoverCardapioItemAdminResult> ExecuteAsync(RemoverCardapioItemAdminCommand command)
    {
        var item = await cardapioRepository.GetByIdAndScopeAsync(command.StorefrontId, command.ItemId, command.EmpresaId)
            ?? throw new CardapioItemNaoEncontradoException(command.StorefrontId, command.ItemId);

        var fotoUrl = item.FotoUrl;

        await cardapioRepository.RemoveAsync(item);
        await unitOfWork.CommitAsync();

        // Best-effort após o item sair do banco — falha de storage não desfaz a remoção.
        await TryDeleteFotoAsync(fotoUrl);

        return new RemoverCardapioItemAdminResult(item.Id);
    }

    private async Task TryDeleteFotoAsync(string? fotoUrl)
    {
        var storageKey = StorageKeyExtractor.Extract(fotoUrl);
        if (storageKey is null) return;

        try
        {
            await fileStorage.DeleteAsync(storageKey);
        }
        catch
        {
            // Silencioso: limpeza residual fica a cargo do job de GC de arquivos órfãos.
        }
    }
}
