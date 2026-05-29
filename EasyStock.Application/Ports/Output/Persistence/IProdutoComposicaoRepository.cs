namespace EasyStock.Application.Ports.Output.Persistence;

public interface IProdutoComposicaoRepository
{
    /// <summary>
    /// Linhas da receita de um produto-final. Tenta override por loja, fallback receita padrao (LojaId null).
    /// Se <paramref name="lojaId"/> e null, retorna direto a receita padrao.
    /// </summary>
    Task<IReadOnlyCollection<ProdutoComposicao>> GetByProdutoFinalAsync(Guid empresaId, Guid produtoFinalId, Guid? lojaId, CancellationToken ct = default);

    /// <summary>
    /// Batch da Onda 1 (cesta in-context): traz receitas de varios produtos-finais numa unica query.
    /// Mesma logica de override-vs-padrao do <see cref="GetByProdutoFinalAsync"/>, aplicada por produto.
    /// Inclui Produto.RendimentoBase/RendimentoUnidade do produto-final (necessario pro calculo do fator) e Insumo (nome + UnidadeMedidaBase).
    /// Produto-final sem receita simplesmente nao aparece no dicionario (consumer usa TryGetValue + marca como SemReceita).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>> GetByProdutosFinaisAsync(Guid empresaId, IReadOnlyList<Guid> produtoFinaisIds, Guid? lojaId, CancellationToken ct = default);

    /// <summary>Receitas que usam este produto como insumo. Util pra alerta de impacto antes de editar.</summary>
    Task<IReadOnlyCollection<ProdutoComposicao>> GetOndeInsumoAsync(Guid empresaId, Guid insumoId, CancellationToken ct = default);

    /// <summary>Detecta se adicionar a linha (produtoFinal, insumoCandidato) criaria ciclo no grafo da empresa.</summary>
    Task<bool> ExisteCicloAsync(Guid empresaId, Guid produtoFinalId, Guid insumoCandidatoId, CancellationToken ct = default);

    Task InsertAsync(ProdutoComposicao composicao, CancellationToken ct = default);

    /// <summary>Remove todas as linhas de uma receita (escopo: produtoFinal + loja) — usado no replace-all.</summary>
    Task DeleteByProdutoFinalAsync(Guid empresaId, Guid produtoFinalId, Guid? lojaId, CancellationToken ct = default);

    /// <summary>
    /// Lista os Produtos que tem pelo menos 1 linha de composicao cadastrada (= sao produto-final
    /// em alguma receita) cujo nome casa com <paramref name="termo"/>. Usado pelo endpoint
    /// /produtos-com-receita do PWA.
    /// </summary>
    Task<IReadOnlyList<Produto>> BuscarProdutosFinaisAsync(Guid empresaId, string? termo, int limit, Guid? lojaId, CancellationToken ct = default);
}
