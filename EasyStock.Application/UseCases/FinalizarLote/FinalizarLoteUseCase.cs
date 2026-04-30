using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.FinalizarLote;

public sealed record FinalizarLoteCommand(Guid EmpresaId, Guid Id);

/// <summary>
/// Finaliza o lote: gera 1 etiqueta por unidade produzida (sequencial 1..N
/// dentro do lote), congela alterações de itens. Idempotente: se já estava
/// finalizado, retorna o estado atual.
/// </summary>
public class FinalizarLoteUseCase(
    ILoteRepository repo,
    IUnitOfWork uow,
    ILogger<FinalizarLoteUseCase> logger)
{
    public async Task<LoteResult?> ExecuteAsync(FinalizarLoteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.Id, "Id");

        var lote = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.Id);
        if (lote == null) return null;
        if (lote.EstaFinalizado) return CriarLoteUseCase.Map(lote);

        if (!lote.Itens.Any())
            throw new UseCaseValidationException("Lote sem itens não pode ser finalizado.");

        // Gera etiquetas: 1 por unidade. Sequencial é global no lote (1..N).
        int seq = 0;
        foreach (var item in lote.Itens)
        {
            for (int u = 1; u <= item.Quantidade; u++)
            {
                seq++;
                var etq = new LoteEtiqueta
                {
                    Id = Guid.NewGuid(),
                    LoteId = lote.Id,
                    LoteItemId = item.Id,
                    Sequencial = seq,
                    Codigo = $"{lote.Codigo}-{seq:D4}",
                    Status = "pendente",
                    CriadoEm = DateTime.UtcNow
                };
                await repo.AddEtiquetaAsync(etq);
                lote.Etiquetas.Add(etq);
            }
        }

        lote.Finalizar();
        await repo.UpdateAsync(lote);
        await uow.CommitAsync();

        logger.LogInformation("Lote {Id} ({Codigo}) finalizado: {N} etiquetas geradas.",
            lote.Id, lote.Codigo, seq);
        return CriarLoteUseCase.Map(lote);
    }
}
