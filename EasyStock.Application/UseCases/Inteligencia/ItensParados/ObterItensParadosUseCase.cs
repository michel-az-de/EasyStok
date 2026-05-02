using System.Diagnostics;
using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Inteligencia.ItensParados;

public class ObterItensParadosUseCase(
    IItemEstoqueRepository itemEstoqueRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IEasyStockConfiguracoes config,
    ILogger<ObterItensParadosUseCase> logger)
{
    public async Task<(IEnumerable<ItensParadosResult> Items, int Total)> ExecuteAsync(ObterItensParadosCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var configuracao = cmd.LojaId.HasValue
            ? await configuracaoRepository.GetByLojaIdAsync(cmd.LojaId.Value).ConfigureAwait(false)
            : null;

        var diasEfetivos = cmd.DiasSemMovimento
            ?? configuracao?.DiasAlertaParado
            ?? config.DiasItemParado;

        var (items, totalCount) = await itemEstoqueRepository.GetItensParadosAsync(
            cmd.EmpresaId, diasEfetivos, cmd.Page, cmd.PageSize, cmd.LojaId).ConfigureAwait(false);

        stopwatch.Stop();

        var resultados = items.Select(item =>
        {
            dynamic dinamico = item;
            var diasSemMovimento = (int)Math.Ceiling((DateTime.UtcNow - (DateTime)dinamico.UltimaMovimentacao).TotalDays);
            return new ItensParadosResult(
                (Guid)dinamico.Id,
                (Guid)dinamico.ProdutoId,
                (string?)dinamico.Produto?.Nome ?? (string?)dinamico.CodigoInterno,
                (string?)dinamico.CodigoInterno,
                (decimal)dinamico.QuantidadeAtual,
                (DateTime)dinamico.UltimaMovimentacao,
                diasSemMovimento);
        });

        logger.LogInformation(
            "Itens parados obtidos em {Ms}ms para empresa {EmpresaId} | "
            + "Dias: {Dias}, Loja: {LojaId}, Total: {Total}",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            diasEfetivos,
            cmd.LojaId ?? Guid.Empty,
            totalCount);

        return (resultados, totalCount);
    }
}
