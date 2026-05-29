using System.Diagnostics;

namespace EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;

public class CalcularProjecaoRupturaUseCase(
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    ILogger<CalcularProjecaoRupturaUseCase> logger)
{
    public async Task<(IEnumerable<ProjecaoRupturaResult> Items, int Total)> ExecuteAsync(CalcularProjecaoRupturaCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var (itens, totalCount) = await itemEstoqueRepository.GetItensEstoquePaginadosAsync(
            cmd.EmpresaId, cmd.Page, cmd.PageSize).ConfigureAwait(false);

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-30);
        var itensLista = itens.ToList();

        var taxasPorProduto = await movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            cmd.EmpresaId, itensLista.Select(i => i.ProdutoId), de, ate).ConfigureAwait(false);

        var projecoes = itensLista.Select(item =>
        {
            dynamic dinamico = item;
            var produtoId = (Guid)dinamico.ProdutoId;
            var taxaDiaria = taxasPorProduto.TryGetValue(produtoId, out var taxa) ? taxa : 0m;
            var diasAteRuptura = taxaDiaria > 0
                ? (int?)Math.Floor((decimal)dinamico.QuantidadeAtual / taxaDiaria)
                : null;

            return new ProjecaoRupturaResult(
                (Guid)dinamico.Id,
                produtoId,
                (string?)dinamico.Produto?.Nome ?? (string?)dinamico.CodigoInterno,
                (string?)dinamico.CodigoInterno,
                (decimal)dinamico.QuantidadeAtual,
                Math.Round(taxaDiaria, 2),
                diasAteRuptura,
                diasAteRuptura.HasValue ? (DateTime?)DateTime.UtcNow.AddDays(diasAteRuptura.Value) : null);
        }).OrderBy(p => p.DiasAteRuptura ?? int.MaxValue);

        stopwatch.Stop();

        logger.LogInformation(
            "Projeção de ruptura calculada em {Ms}ms para {Count} itens da empresa {EmpresaId}",
            stopwatch.ElapsedMilliseconds,
            itensLista.Count,
            cmd.EmpresaId);

        return (projecoes, totalCount);
    }
}
