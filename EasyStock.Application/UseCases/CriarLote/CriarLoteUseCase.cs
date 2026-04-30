using System.ComponentModel.DataAnnotations;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Lotes;
using EasyStock.Domain.Entities;
using Microsoft.Extensions.Logging;
using LoteEntity = EasyStock.Domain.Entities.Lote;

namespace EasyStock.Application.UseCases.CriarLote;

public sealed record CriarLoteItemInput(
    [property: Required][property: MaxLength(150)] string Nome,
    int Quantidade,
    Guid? ProdutoId = null,
    [property: MaxLength(16)] string? Emoji = null,
    [property: MaxLength(32)] string? Unidade = null,
    int? PesoG = null,
    int? ValidadeDias = null,
    string? FotoUrl = null);

public sealed record CriarLoteCommand(
    [property: Required] Guid EmpresaId,
    Guid? LojaId = null,
    DateTime? DataProducao = null,
    [property: MaxLength(40)] string? CodigoCustom = null,
    Guid? OperadorUserId = null,
    [property: MaxLength(120)] string? OperadorNome = null,
    string? Observacoes = null,
    string? FotoUrl = null,
    [property: MaxLength(20)] string? Origem = "web",
    [property: MaxLength(64)] string? MobileBatchId = null,
    IReadOnlyList<CriarLoteItemInput>? Itens = null);

/// <summary>
/// Cria um lote — gera código sequencial do dia (LOT-YYMMDD-NNN) se não vier
/// custom. Não gera etiquetas ainda — só ao Finalizar.
/// </summary>
public class CriarLoteUseCase(
    ILoteRepository repo,
    IUnitOfWork uow,
    ILogger<CriarLoteUseCase> logger)
{
    public async Task<LoteResult> ExecuteAsync(CriarLoteCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var dataProd = cmd.DataProducao ?? DateTime.UtcNow;
        var data = DateOnly.FromDateTime(dataProd);

        string codigo;
        if (!string.IsNullOrWhiteSpace(cmd.CodigoCustom))
        {
            codigo = cmd.CodigoCustom.Trim();
            var existe = await repo.FindByCodigoAsync(cmd.EmpresaId, codigo);
            if (existe != null)
                throw new UseCaseValidationException($"Código '{codigo}' já existe.");
        }
        else
        {
            var seq = await repo.GetNextSequencialDoDiaAsync(cmd.EmpresaId, data);
            codigo = $"LOT-{data:yyMMdd}-{seq:D3}";
        }

        var lote = LoteEntity.Criar(cmd.EmpresaId, codigo, dataProd, cmd.LojaId);
        lote.OperadorUserId = cmd.OperadorUserId;
        lote.OperadorNome = cmd.OperadorNome;
        lote.Observacoes = cmd.Observacoes;
        lote.FotoUrl = cmd.FotoUrl;
        lote.Origem = cmd.Origem;
        lote.MobileBatchId = cmd.MobileBatchId;

        if (cmd.Itens != null)
        {
            foreach (var input in cmd.Itens)
            {
                if (input.Quantidade <= 0)
                    throw new UseCaseValidationException("Quantidade deve ser maior que zero.");

                var expira = input.ValidadeDias.HasValue ? lote.DataProducao.AddDays(input.ValidadeDias.Value) : (DateTime?)null;
                lote.Itens.Add(new LoteItem
                {
                    Id = Guid.NewGuid(),
                    LoteId = lote.Id,
                    ProdutoId = input.ProdutoId,
                    Nome = input.Nome.Trim(),
                    Emoji = input.Emoji,
                    Unidade = input.Unidade,
                    Quantidade = input.Quantidade,
                    PesoG = input.PesoG,
                    ValidadeDias = input.ValidadeDias,
                    ExpiraEm = expira,
                    FotoUrl = input.FotoUrl,
                    CriadoEm = DateTime.UtcNow
                });
            }
        }

        await repo.AddAsync(lote);
        await uow.CommitAsync();

        logger.LogInformation("Lote {Id} ({Codigo}) criado com {N} item(s).", lote.Id, codigo, lote.Itens.Count);
        return Map(lote);
    }

    internal static LoteResult Map(LoteEntity l)
    {
        var conferidas = l.Etiquetas.Count(e => e.Status == "conferida");
        var divergentes = l.Etiquetas.Count(e => e.Status == "divergente");
        return new LoteResult(
            l.Id, l.EmpresaId, l.LojaId, l.Codigo, l.Status, l.DataProducao,
            l.OperadorUserId, l.OperadorNome, l.Observacoes, l.FotoUrl,
            l.Origem, l.MobileBatchId,
            l.Itens.Count, l.TotalUnidades,
            conferidas, divergentes,
            l.CriadoEm, l.AlteradoEm, l.FinalizadoEm);
    }
}
