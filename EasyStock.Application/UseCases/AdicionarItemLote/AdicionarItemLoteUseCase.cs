using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.CriarLote;
using EasyStock.Application.UseCases.Lotes;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.AdicionarItemLote;

public sealed record AdicionarItemLoteCommand(
    [property: Required] Guid EmpresaId,
    [property: Required] Guid LoteId,
    [property: Required][property: MaxLength(150)] string Nome,
    int Quantidade,
    Guid? ProdutoId = null,
    [property: MaxLength(16)] string? Emoji = null,
    [property: MaxLength(32)] string? Unidade = null,
    int? PesoG = null,
    int? ValidadeDias = null,
    string? FotoUrl = null);

public class AdicionarItemLoteUseCase(
    ILoteRepository repo,
    IUnitOfWork uow,
    ILogger<AdicionarItemLoteUseCase> logger)
{
    public async Task<LoteResult?> ExecuteAsync(AdicionarItemLoteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        UseCaseGuards.EnsureNotEmpty(cmd.LoteId, "LoteId");
        if (cmd.Quantidade <= 0)
            throw new UseCaseValidationException("Quantidade deve ser maior que zero.");

        var lote = await repo.GetByIdWithDetailsAsync(cmd.EmpresaId, cmd.LoteId);
        if (lote == null) return null;
        if (lote.EstaFinalizado)
            throw new UseCaseValidationException("Não é permitido alterar itens de lote finalizado.");

        var item = new LoteItem
        {
            Id = Guid.NewGuid(),
            LoteId = lote.Id,
            ProdutoId = cmd.ProdutoId,
            Nome = cmd.Nome.Trim(),
            Emoji = cmd.Emoji,
            Unidade = cmd.Unidade,
            Quantidade = cmd.Quantidade,
            PesoG = cmd.PesoG,
            ValidadeDias = cmd.ValidadeDias,
            ExpiraEm = cmd.ValidadeDias.HasValue ? lote.DataProducao.AddDays(cmd.ValidadeDias.Value) : null,
            FotoUrl = cmd.FotoUrl,
            CriadoEm = DateTime.UtcNow
        };

        await repo.AddItemAsync(item);
        lote.Itens.Add(item);
        lote.AlteradoEm = DateTime.UtcNow;
        await repo.UpdateAsync(lote);
        await uow.CommitAsync();

        logger.LogInformation("Lote {Id}: item {Item} (+{Qty}) adicionado.", lote.Id, item.Nome, item.Quantidade);
        return CriarLoteUseCase.Map(lote);
    }
}
