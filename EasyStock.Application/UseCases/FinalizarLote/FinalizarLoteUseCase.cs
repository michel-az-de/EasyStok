using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
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
    IProdutoRepository produtoRepo,
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

        // C2 (RDC 727/2022): bloqueia finalização se algum item de produto Embalado
        // estiver sem peso. Itens de produto Avulso passam sem peso.
        // Cobre lotes legados (LOT-260516) que entraram antes da migration.
        var produtoIdsLote = lote.Itens
            .Where(i => i.ProdutoId.HasValue)
            .Select(i => i.ProdutoId!.Value)
            .Distinct()
            .ToList();
        var tipoEmbalagemMap = produtoIdsLote.Count > 0
            ? await produtoRepo.GetTipoEmbalagemMapAsync(cmd.EmpresaId, produtoIdsLote)
            : new Dictionary<Guid, TipoEmbalagem>();
        var itensEmbalagemSemPeso = lote.Itens.Where(i =>
            i.ProdutoId.HasValue
            && tipoEmbalagemMap.TryGetValue(i.ProdutoId.Value, out var t)
            && t == TipoEmbalagem.Embalado
            && (!i.PesoG.HasValue || i.PesoG.Value <= 0)
        ).ToList();
        if (itensEmbalagemSemPeso.Count > 0)
        {
            var nomes = string.Join(", ", itensEmbalagemSemPeso.Select(i => i.Nome));
            throw new UseCaseValidationException(
                $"Não é possível finalizar: {itensEmbalagemSemPeso.Count} item(ns) embalado(s) " +
                $"sem peso ({nomes}). RDC 727 exige peso na rotulagem. " +
                $"Edite o lote e informe o peso antes de finalizar.");
        }

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
