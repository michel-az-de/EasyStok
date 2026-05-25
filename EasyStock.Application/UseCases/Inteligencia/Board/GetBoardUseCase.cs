using System.Diagnostics;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Inteligencia.Board;

public class GetBoardUseCase(
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    ILogger<GetBoardUseCase> logger)
{
    public async Task<GetBoardResult> ExecuteAsync(GetBoardCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-cmd.Periodo);

        var taxaDiariaTask = movimentacaoRepository.GetTaxaSaidaDiariaAsync(cmd.EmpresaId, null, de, ate);
        var resumoEstoqueTask = itemEstoqueRepository.GetResumoEstoqueAsync(cmd.EmpresaId);
        await Task.WhenAll(taxaDiariaTask, resumoEstoqueTask).ConfigureAwait(false);

        var taxaDiaria = await taxaDiariaTask;
        var resumoEstoque = await resumoEstoqueTask;

        stopwatch.Stop();

        var projecaoVendas = Math.Round(taxaDiaria * cmd.Periodo, 0);
        var projecaoReceita = Math.Round(taxaDiaria * cmd.Periodo * resumoEstoque.TicketMedioSugerido, 2);

        logger.LogInformation(
            "Board retrieved in {Ms}ms for empresa {EmpresaId} | "
            + "Estoque: {Quantidade} un, "
            + "Média: {TaxaDiaria}/dia, "
            + "Projeção {Periodo}d: {ProjecaoVendas} un, R${ProjecaoReceita}",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            resumoEstoque.QuantidadeEmEstoque,
            Math.Round(taxaDiaria, 2),
            cmd.Periodo,
            projecaoVendas,
            projecaoReceita);

        return new GetBoardResult(
            cmd.EmpresaId,
            cmd.Periodo,
            resumoEstoque.QuantidadeEmEstoque,
            Math.Round(resumoEstoque.ValorTotalEstoque, 2),
            Math.Round(taxaDiaria, 2),
            projecaoVendas,
            projecaoReceita);
    }
}
