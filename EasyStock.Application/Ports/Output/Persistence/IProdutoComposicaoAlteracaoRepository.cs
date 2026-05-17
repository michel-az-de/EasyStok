using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IProdutoComposicaoAlteracaoRepository
{
    Task AddAsync(ProdutoComposicaoAlteracao alteracao, CancellationToken ct = default);

    Task<IReadOnlyList<ProdutoComposicaoAlteracao>> GetByProdutoFinalAsync(Guid empresaId, Guid produtoFinalId, int limit = 100, CancellationToken ct = default);
}
