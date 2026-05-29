using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;

namespace EasyStock.Application.UseCases.RemoverItemLote;

public sealed record RemoverItemLoteCommand(Guid EmpresaId, Guid LoteId, Guid ItemId);

public class RemoverItemLoteUseCase(
    ILoteRepository repo,
    IUnitOfWork uow,
    ILogger<RemoverItemLoteUseCase> logger)
{
    public async Task<LoteResult?> ExecuteAsync(RemoverItemLoteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.LoteId, "LoteId");
        UseCaseGuards.EnsureNotEmpty(cmd.ItemId, "ItemId");

        var lote = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.LoteId);
        if (lote == null) return null;
        if (lote.EstaFinalizado)
            throw new UseCaseValidationException("Não é permitido remover item de lote finalizado.");

        var item = lote.Itens.FirstOrDefault(i => i.Id == cmd.ItemId);
        if (item == null) return CriarLoteUseCase.Map(lote);

        await repo.RemoveItemAsync(item.Id);
        lote.Itens.Remove(item);
        lote.AlteradoEm = DateTime.UtcNow;
        await repo.UpdateAsync(lote);
        await uow.CommitAsync();

        logger.LogInformation("Lote {Id}: item {Item} removido.", lote.Id, item.Nome);
        return CriarLoteUseCase.Map(lote);
    }
}
