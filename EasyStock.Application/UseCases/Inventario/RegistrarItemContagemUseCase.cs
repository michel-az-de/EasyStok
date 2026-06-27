using EasyStock.Domain.ValueObjects;

namespace EasyStock.Application.UseCases.Inventario;

public sealed record RegistrarItemContagemCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid UsuarioId,
    [property: Required] Guid ItemContagemId,
    decimal QtdContada);

public sealed record ItemContagemResult(
    Guid Id,
    Guid ProdutoId,
    Guid? ItemEstoqueId,
    decimal? QtdSistemaNoMomento,
    decimal? QtdContada,
    decimal? Divergencia,
    bool Conferido,
    DateTime? ContadoEm);

/// <summary>
/// Autosave de um lote contado. Captura o BASELINE do sistema NO MOMENTO
/// (QtdSistemaNoMomento = QuantidadeAtual atual do lote) — ancora do delta que preserva
/// venda concorrente entre contar e aplicar. Lote descoberto (ItemEstoqueId null): baseline = 0.
/// Online-only: conflito de xmin (2 operadores no mesmo lote) sobe DbUpdateConcurrencyException,
/// que a Api (Fatia 4) mapeia para 409 — a Application NAO referencia EF.
/// </summary>
public class RegistrarItemContagemUseCase(
    IContagemRepository repo,
    IItemEstoqueRepository itemEstoqueRepository,
    IUnitOfWork uow,
    ILogger<RegistrarItemContagemUseCase> logger)
{
    public async Task<ItemContagemResult> ExecuteAsync(RegistrarItemContagemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.UsuarioId, "UsuarioId");
        UseCaseGuards.EnsureNotEmpty(cmd.ItemContagemId, "ItemContagemId");
        if (cmd.QtdContada < 0)
            throw new UseCaseValidationException("QtdContada nao pode ser negativa.");

        var item = await repo.GetItemAsync(cmd.EmpresaId, cmd.ItemContagemId)
            ?? throw new UseCaseValidationException("Item de contagem nao encontrado.");

        // Baseline do sistema NO MOMENTO (ancora do delta). Lote descoberto: esperado = 0.
        var qtdSistema = Quantidade.Zero;
        Dinheiro? custo = null;
        if (item.ItemEstoqueId.HasValue)
        {
            var lote = await itemEstoqueRepository.GetByIdAsync(cmd.EmpresaId, item.ItemEstoqueId.Value)
                ?? throw new UseCaseValidationException("Lote do item de contagem nao encontrado.");
            qtdSistema = lote.QuantidadeAtual;
            custo = lote.CustoUnitario;
        }

        item.Contar(Quantidade.From(cmd.QtdContada), qtdSistema, custo, cmd.UsuarioId, DateTime.UtcNow);
        await uow.CommitAsync(); // conflito xmin -> DbUpdateConcurrencyException -> Api mapeia 409

        logger.LogDebug("ItemContagem {Id} contado (qtd {Qtd}) por {Usuario}.",
            item.Id, cmd.QtdContada, cmd.UsuarioId);
        return MapItem(item);
    }

    internal static ItemContagemResult MapItem(ItemContagem i) => new(
        i.Id, i.ProdutoId, i.ItemEstoqueId,
        i.QtdSistemaNoMomento?.Value, i.QtdContada?.Value, i.Divergencia, i.Conferido, i.ContadoEm);
}
