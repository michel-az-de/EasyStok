namespace EasyStock.Application.UseCases.GerenciarProduto.Queries;

/// <summary>
/// Query: calcula estatisticas operacionais do produto:
/// - quantidade em estoque + custo medio (custo_total / quantidade)
/// - margem real (preco_referencia vs custo medio em %)
/// - velocidades de saida diaria em 30 / 60 / 90 dias
/// - previsao de dias ate zeramento (floor(quantidade / vel30))
/// - sazonalidade mensal dos ultimos 12 meses
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9b). O facade
/// continua expondo <c>ObterEstatisticasAsync</c> via delegacao, preservando
/// contrato publico (R8).
/// </summary>
public sealed class ObterEstatisticasProdutoUseCase(
    IProdutoRepository produtoRepository,
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository)
{
    public async Task<ProdutoEstatisticasResult> ExecuteAsync(Guid empresaId, Guid produtoId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        var produto = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var itens = await itemEstoqueRepository.GetByProdutoAsync(empresaId, produtoId);
        var quantidade = itens.Sum(i => i.QuantidadeAtual.Value);
        var custoTotal = itens.Sum(i => i.CustoUnitario.Valor * i.QuantidadeAtual.Value);
        var custoMedio = quantidade > 0 ? (decimal?)(custoTotal / quantidade) : produto.CustoReferencia?.Valor;
        var precoReferencia = produto.PrecoReferencia?.Valor;
        var margemReal = precoReferencia.HasValue && precoReferencia.Value > 0m && custoMedio.HasValue
            ? (decimal?)decimal.Round(((precoReferencia.Value - custoMedio.Value) / precoReferencia.Value) * 100m, 2)
            : null;

        var agora = DateTime.UtcNow;
        var vel30 = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, agora.AddDays(-30), agora);
        var vel60 = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, agora.AddDays(-60), agora);
        var vel90 = await movimentacaoEstoqueRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, agora.AddDays(-90), agora);
        var previsao = vel30 <= 0m ? null : (int?)Math.Floor(quantidade / vel30);
        var sazonalidade = await movimentacaoEstoqueRepository.GetAgregacaoMensalAsync(empresaId, produtoId, 12);

        return new ProdutoEstatisticasResult(
            produtoId,
            quantidade,
            margemReal,
            decimal.Round(vel30, 2),
            previsao,
            decimal.Round(vel60, 2),
            decimal.Round(vel90, 2),
            sazonalidade
                .Select(x => new SazonalidadeMensalResult(x.Ano, x.Mes, x.TotalSaidas, x.ValorTotal))
                .ToArray());
    }
}
