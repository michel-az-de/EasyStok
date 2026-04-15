using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public class ProdutoAlteracaoRepository(EasyStockDbContext context) : IProdutoAlteracaoRepository
{
    public Task AddAsync(ProdutoAlteracao alteracao)
    {
        context.ProdutoAlteracoes.Add(alteracao);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<ProdutoAlteracaoResumo>> GetByProdutoAsync(Guid empresaId, Guid produtoId, int limit = 100)
    {
        return await context.ProdutoAlteracoes
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.ProdutoId == produtoId)
            .OrderByDescending(a => a.AlteradoEm)
            .Take(limit)
            .Select(a => new ProdutoAlteracaoResumo(
                a.Id,
                a.Acao,
                a.UsuarioId,
                a.Usuario != null ? a.Usuario.Nome : null,
                a.AlteracoesJson,
                a.AlteradoEm))
            .ToListAsync();
    }
}
