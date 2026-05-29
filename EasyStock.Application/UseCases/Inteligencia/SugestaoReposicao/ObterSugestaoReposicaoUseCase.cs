using System.Diagnostics;
using EasyStock.Application.Configuration;

namespace EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;

public class ObterSugestaoReposicaoUseCase(
    IItemEstoqueRepository itemEstoqueRepository,
    IConfiguracaoLojaRepository configuracaoRepository,
    IEasyStockConfiguracoes config,
    ILogger<ObterSugestaoReposicaoUseCase> logger)
{
    public async Task<(IEnumerable<SugestaoReposicaoResult> Items, int Total)> ExecuteAsync(ObterSugestaoReposicaoCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var configuracao = cmd.LojaId.HasValue
            ? await configuracaoRepository.GetByLojaIdAsync(cmd.LojaId.Value).ConfigureAwait(false)
            : null;

        var limiteEfetivo = cmd.LimiteQuantidade
            ?? configuracao?.QuantidadeMinimaPadrao
            ?? config.LimiteEstoqueBaixoDefault;

        var (items, totalCount) = await itemEstoqueRepository.GetSugestaoReposicaoAsync(
            cmd.EmpresaId, limiteEfetivo, cmd.Page, cmd.PageSize, cmd.LojaId).ConfigureAwait(false);

        stopwatch.Stop();

        var resultados = items.Select(item =>
        {
            dynamic dinamico = item;
            return new SugestaoReposicaoResult(
                (Guid)dinamico.Id,
                (Guid)dinamico.ProdutoId,
                (string?)dinamico.Produto?.Nome ?? (string?)dinamico.CodigoInterno,
                (string?)dinamico.CodigoInterno,
                (decimal)dinamico.QuantidadeAtual,
                (int)limiteEfetivo,
                (decimal)dinamico.QuantidadeSugerida,
                (decimal)dinamico.CustoEstimado);
        });

        logger.LogInformation(
            "Sugestão de reposição obtida em {Ms}ms para empresa {EmpresaId} | "
            + "Limite: {Limite}, Loja: {LojaId}, Total: {Total}",
            stopwatch.ElapsedMilliseconds,
            cmd.EmpresaId,
            limiteEfetivo,
            cmd.LojaId ?? Guid.Empty,
            totalCount);

        return (resultados, totalCount);
    }
}
