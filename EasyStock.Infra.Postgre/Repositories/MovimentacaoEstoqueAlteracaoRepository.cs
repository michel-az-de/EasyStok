using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public class MovimentacaoEstoqueAlteracaoRepository(EasyStockDbContext context) : IMovimentacaoEstoqueAlteracaoRepository
{
    public Task AddAsync(MovimentacaoEstoqueAlteracao alteracao)
    {
        context.MovimentacaoEstoqueAlteracoes.Add(alteracao);
        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<MovimentacaoEstoqueAlteracaoResumo>> GetByMovimentacaoAsync(
        Guid empresaId, Guid movimentacaoId, int limit = 100)
    {
        return await context.MovimentacaoEstoqueAlteracoes
            .AsNoTracking()
            .Where(a => a.EmpresaId == empresaId && a.MovimentacaoEstoqueId == movimentacaoId)
            .OrderByDescending(a => a.AlteradoEm)
            .Take(limit)
            .Select(a => new MovimentacaoEstoqueAlteracaoResumo(
                a.Id,
                a.Acao,
                a.UsuarioId,
                a.NomeUsuario,
                a.EmailUsuario,
                a.AlteracoesJson,
                a.Ip,
                a.UserAgent,
                a.AlteradoEm,
                a.Motivo,
                a.Observacao))
            .ToListAsync();
    }

    public async Task<IReadOnlyList<MovimentacaoEstoqueAlteracaoResumo>> GetByUsuarioAsync(
        Guid usuarioId, DateTime desde, DateTime ate, int pageSize = 50)
    {
        return await context.MovimentacaoEstoqueAlteracoes
            .AsNoTracking()
            .Where(a => a.UsuarioId == usuarioId && a.AlteradoEm >= desde && a.AlteradoEm <= ate)
            .OrderByDescending(a => a.AlteradoEm)
            .Take(pageSize)
            .Select(a => new MovimentacaoEstoqueAlteracaoResumo(
                a.Id,
                a.Acao,
                a.UsuarioId,
                a.NomeUsuario,
                a.EmailUsuario,
                a.AlteracoesJson,
                a.Ip,
                a.UserAgent,
                a.AlteradoEm,
                a.Motivo,
                a.Observacao))
            .ToListAsync();
    }

    public async Task<int> DeleteByUsuarioAsync(Guid usuarioId)
    {
        return await context.MovimentacaoEstoqueAlteracoes
            .Where(a => a.UsuarioId == usuarioId)
            .ExecuteDeleteAsync();
    }
}
