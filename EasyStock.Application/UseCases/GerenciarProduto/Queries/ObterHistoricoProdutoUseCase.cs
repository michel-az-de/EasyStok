namespace EasyStock.Application.UseCases.GerenciarProduto.Queries;

/// <summary>
/// Query: lista o historico de movimentacoes de estoque do produto (entradas, saidas,
/// transferencias, ajustes), ordenado por data desc pelo repo.
///
/// Extraido do god-UseCase <c>GerenciarProdutoUseCase</c> (F9b). O facade continua
/// expondo <c>ObterHistoricoAsync</c> via delegacao, preservando contrato publico (R8).
/// </summary>
public sealed class ObterHistoricoProdutoUseCase(
    IProdutoRepository produtoRepository,
    IMovimentacaoEstoqueRepository movimentacaoEstoqueRepository)
{
    public async Task<IReadOnlyCollection<ProdutoHistoricoItemResult>> ExecuteAsync(Guid empresaId, Guid produtoId)
    {
        UseCaseGuards.EnsureEmpresaId(empresaId);
        UseCaseGuards.EnsureNotEmpty(produtoId, "ProdutoId");

        _ = await produtoRepository.GetByIdAsync(empresaId, produtoId)
            ?? throw new UseCaseValidationException("Produto nao encontrado.");

        var historico = await movimentacaoEstoqueRepository.GetByProdutoAsync(empresaId, produtoId);
        return historico
            .Select(m => new ProdutoHistoricoItemResult(
                m.Id,
                m.Tipo,
                m.Natureza.ToString(),
                m.Quantidade.Value,
                m.ValorTotal?.Valor,
                m.DataMovimentacao,
                m.ItemEstoqueId,
                m.DocumentoReferencia,
                m.Descricao))
            .ToArray();
    }
}
