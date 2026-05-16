using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IProdutoComposicaoRepository
{
    /// <summary>
    /// Linhas da receita de um produto-final. Tenta override por loja, fallback receita padrao (LojaId null).
    /// Se <paramref name="lojaId"/> e null, retorna direto a receita padrao.
    /// </summary>
    Task<IReadOnlyCollection<ProdutoComposicao>> GetByProdutoFinalAsync(Guid empresaId, Guid produtoFinalId, Guid? lojaId, CancellationToken ct = default);

    /// <summary>Receitas que usam este produto como insumo. Util pra alerta de impacto antes de editar.</summary>
    Task<IReadOnlyCollection<ProdutoComposicao>> GetOndeInsumoAsync(Guid empresaId, Guid insumoId, CancellationToken ct = default);

    /// <summary>Detecta se adicionar a linha (produtoFinal, insumoCandidato) criaria ciclo no grafo da empresa.</summary>
    Task<bool> ExisteCicloAsync(Guid empresaId, Guid produtoFinalId, Guid insumoCandidatoId, CancellationToken ct = default);

    Task InsertAsync(ProdutoComposicao composicao, CancellationToken ct = default);

    /// <summary>Remove todas as linhas de uma receita (escopo: produtoFinal + loja) — usado no replace-all.</summary>
    Task DeleteByProdutoFinalAsync(Guid empresaId, Guid produtoFinalId, Guid? lojaId, CancellationToken ct = default);
}
