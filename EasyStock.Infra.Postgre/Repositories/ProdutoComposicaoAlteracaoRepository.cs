using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public class ProdutoComposicaoAlteracaoRepository(EasyStockDbContext context) : IProdutoComposicaoAlteracaoRepository
{
    public Task AddAsync(ProdutoComposicaoAlteracao alteracao, CancellationToken ct = default)
    {
        context.ProdutosComposicaoAlteracoes.Add(alteracao);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ProdutoComposicaoAlteracao>> GetByProdutoFinalAsync(
        Guid empresaId, Guid produtoFinalId, int limit = 100, CancellationToken ct = default)
    {
        return await context.ProdutosComposicaoAlteracoes
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.ProdutoFinalId == produtoFinalId)
            .OrderByDescending(a => a.AlteradoEm)
            .Take(limit)
            .ToListAsync(ct);
    }
}
