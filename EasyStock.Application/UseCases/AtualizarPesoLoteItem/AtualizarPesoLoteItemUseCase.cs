namespace EasyStock.Application.UseCases.AtualizarPesoLoteItem;

/// <summary>
/// C2 (correcao backfill LOT-260516): permite editar o peso de um item de
/// lote APENAS enquanto o lote esta em producao. Apos finalizacao, peso e
/// imutavel para preservar auditoria RDC (etiqueta ja impressa).
/// </summary>
public sealed record AtualizarPesoLoteItemCommand(Guid EmpresaId, Guid LoteId, Guid ItemId, int PesoG);

public sealed record AtualizarPesoLoteItemResult(string Status, string? Codigo);

public class AtualizarPesoLoteItemUseCase(
    ILoteRepository repo,
    IUnitOfWork uow,
    ILogger<AtualizarPesoLoteItemUseCase> logger)
{
    public async Task<AtualizarPesoLoteItemResult?> ExecuteAsync(AtualizarPesoLoteItemCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.LoteId, "LoteId");
        UseCaseGuards.EnsureNotEmpty(cmd.ItemId, "ItemId");

        if (cmd.PesoG <= 0)
            throw new UseCaseValidationException("Peso deve ser maior que zero.");

        var lote = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.LoteId);
        if (lote == null) return null;

        // R3: peso e imutavel apos finalizacao. LayoutSnapshotJson das etiquetas
        // ja impressas eh imutavel por design (Lote.cs:134). Permitir editar
        // PesoG depois romperia o vinculo dado-fisico-etiqueta.
        if (lote.Status != "em_producao")
            throw new UseCaseValidationException(
                $"Lote ja finalizado (status={lote.Status}) — peso e imutavel " +
                "(auditoria RDC 727/2022). Crie novo lote para correcao.");

        var item = lote.Itens.FirstOrDefault(i => i.Id == cmd.ItemId);
        if (item == null) return null;

        var pesoAntigo = item.PesoG;
        item.PesoG = cmd.PesoG;
        lote.AlteradoEm = DateTime.UtcNow;

        await repo.UpdateAsync(lote);
        await uow.CommitAsync();

        logger.LogInformation(
            "Lote {LoteId} item {ItemId}: peso {Antigo} -> {Novo}g (backfill C2).",
            cmd.LoteId, cmd.ItemId, pesoAntigo, cmd.PesoG);
        return new AtualizarPesoLoteItemResult(lote.Status, lote.Codigo);
    }
}
