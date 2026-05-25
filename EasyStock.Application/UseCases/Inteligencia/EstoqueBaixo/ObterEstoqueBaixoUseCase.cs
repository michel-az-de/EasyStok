using System.Diagnostics;
using EasyStock.Application.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;

public class ObterEstoqueBaixoUseCase(
    IItemEstoqueRepository itemEstoqueRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IEasyStockConfiguracoes config,
    ILogger<ObterEstoqueBaixoUseCase> logger)
{
    public async Task<(IEnumerable<EstoqueBaixoResult> Items, int Total)> ExecuteAsync(ObterEstoqueBaixoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var configuracao = cmd.LojaId.HasValue
            ? await configuracaoRepository.GetByLojaIdAsync(cmd.LojaId.Value).ConfigureAwait(false)
            : null;

        var limiteEfetivo = cmd.Limite
            ?? configuracao?.QuantidadeMinimaPadrao
            ?? config.LimiteEstoqueBaixoDefault;

        var (items, totalCount) = await itemEstoqueRepository.GetEstoqueBaixoAsync(
            cmd.EmpresaId, limiteEfetivo, cmd.Page, cmd.PageSize, cmd.LojaId).ConfigureAwait(false);

        stopwatch.Stop();

        var resultados = items.Select(item =>
        {
            dynamic dinamico = item;
            return new EstoqueBaixoResult(
                (Guid)dinamico.Id,
                (Guid)dinamico.ProdutoId,
                (string?)dinamico.Produto?.Nome ?? (string?)dinamico.CodigoInterno,
                (string?)dinamico.CodigoInterno,
                (decimal)dinamico.QuantidadeAtual,
                (int)limiteEfetivo);
        });

        logger.LogInformation(
            "Estoque baixo obtido em {Ms}ms para empresa {EmpresaId} | "
            + "Limite: {Limite}, Loja: {LojaId}, Total: {Total}",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            limiteEfetivo,
            cmd.LojaId ?? Guid.Empty,
            totalCount);

        return (resultados, totalCount);
    }
}
