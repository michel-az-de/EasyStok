using System.Diagnostics;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Inteligencia.Sazonalidade;

public class CalcularSazonalidadeInteligenciaUseCase(
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    ILogger<CalcularSazonalidadeInteligenciaUseCase> logger)
{
    public async Task<IEnumerable<SazonalidadeInteligenciaResult>> ExecuteAsync(CalcularSazonalidadeInteligenciaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);
        if (cmd.ProdutoId == Guid.Empty)
        {
            throw new UseCaseValidationException("ProdutoId não pode ser vazio");
        }

        var stopwatch = Stopwatch.StartNew();

        var dados = await movimentacaoRepository.GetAgregacaoMensalAsync(
            cmd.EmpresaId, cmd.ProdutoId, cmd.Meses).ConfigureAwait(false);

        stopwatch.Stop();

        var resultados = dados.Select(d => new SazonalidadeInteligenciaResult(
            d.Ano,
            d.Mes,
            d.TotalSaidas,
            d.ValorTotal));

        logger.LogInformation(
            "Sazonalidade calculada em {Ms}ms para empresa {EmpresaId}, produto {ProdutoId} | "
            + "{Count} períodos em {Meses} meses",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            cmd.ProdutoId,
            resultados.Count(),
            cmd.Meses);

        return resultados;
    }
}
