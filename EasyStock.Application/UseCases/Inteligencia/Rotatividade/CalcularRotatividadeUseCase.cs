using System.Diagnostics;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Inteligencia.Rotatividade;

public class CalcularRotatividadeUseCase(
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    ILogger<CalcularRotatividadeUseCase> logger)
{
    public async Task<RotatividadeResult> ExecuteAsync(CalcularRotatividadeCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-cmd.DiasHistorico);

        var taxaDiaria = await movimentacaoRepository.GetTaxaSaidaDiariaAsync(
            cmd.EmpresaId, cmd.ProdutoId, de, ate).ConfigureAwait(false);

        stopwatch.Stop();

        var taxaSemanal = Math.Round(taxaDiaria * 7, 2);
        var taxaMensal = Math.Round(taxaDiaria * 30, 2);

        logger.LogInformation(
            "Rotatividade calculada em {Ms}ms para empresa {EmpresaId}, produto: {ProdutoId} | "
            + "Taxa: {TaxaDiaria}/dia, {TaxaSemanal}/semana, {TaxaMensal}/mês",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            cmd.ProdutoId ?? Guid.Empty,
            Math.Round(taxaDiaria, 2),
            taxaSemanal,
            taxaMensal);

        return new RotatividadeResult(
            cmd.EmpresaId,
            cmd.ProdutoId,
            cmd.DiasHistorico,
            Math.Round(taxaDiaria, 2),
            taxaSemanal,
            taxaMensal);
    }
}
