using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public class ProdutoComposicaoRepository(EasyStockDbContext context) : IProdutoComposicaoRepository
{
    public async Task<IReadOnlyCollection<ProdutoComposicao>> GetByProdutoFinalAsync(
        Guid empresaId, Guid produtoFinalId, Guid? lojaId, CancellationToken ct = default)
    {
        var baseQuery = context.ProdutosComposicao
            .AsNoTracking()
            .Include(c => c.Insumo)
            .Where(c => c.EmpresaId == empresaId && c.ProdutoFinalId == produtoFinalId);

        if (lojaId.HasValue)
        {
            // Tenta override da loja primeiro
            var override_ = await baseQuery
                .Where(c => c.LojaId == lojaId.Value)
                .OrderBy(c => c.OrdemExibicao)
                .ToListAsync(ct);

            if (override_.Count > 0)
                return override_;
        }

        // Fallback receita padrao (LojaId null)
        return await baseQuery
            .Where(c => c.LojaId == null)
            .OrderBy(c => c.OrdemExibicao)
            .ToListAsync(ct);
    }

    public Task<IReadOnlyCollection<ProdutoComposicao>> GetOndeInsumoAsync(
        Guid empresaId, Guid insumoId, CancellationToken ct = default) =>
        context.ProdutosComposicao
            .AsNoTracking()
            .Include(c => c.ProdutoFinal)
            .Where(c => c.EmpresaId == empresaId && c.InsumoId == insumoId)
            .OrderBy(c => c.ProdutoFinal!.Nome)
            .ToListAsync(ct)
            .ContinueWith(t => (IReadOnlyCollection<ProdutoComposicao>)t.Result, ct);

    public async Task<bool> ExisteCicloAsync(
        Guid empresaId, Guid produtoFinalId, Guid insumoCandidatoId, CancellationToken ct = default)
    {
        // A->A direto
        if (produtoFinalId == insumoCandidatoId) return true;

        // DFS in-memory: explora grafo a partir do insumo candidato.
        // Se chegar no produtoFinalId em qualquer descendente, ha ciclo.
        var visitados = new HashSet<Guid>();
        var pilha = new Stack<(Guid Id, int Profundidade)>();
        pilha.Push((insumoCandidatoId, 0));

        const int profundidadeMax = 10;

        while (pilha.Count > 0)
        {
            var (atual, prof) = pilha.Pop();
            if (atual == produtoFinalId) return true;
            if (prof >= profundidadeMax) continue;
            if (!visitados.Add(atual)) continue;

            // Filhos do atual = insumos da receita do atual (caso atual seja produto-final em alguma receita)
            var filhos = await context.ProdutosComposicao
                .AsNoTracking()
                .Where(c => c.EmpresaId == empresaId && c.ProdutoFinalId == atual)
                .Select(c => c.InsumoId)
                .ToListAsync(ct);

            foreach (var f in filhos)
                pilha.Push((f, prof + 1));
        }

        return false;
    }

    public Task InsertAsync(ProdutoComposicao composicao, CancellationToken ct = default)
    {
        context.ProdutosComposicao.Add(composicao);
        return Task.CompletedTask;
    }

    public async Task DeleteByProdutoFinalAsync(
        Guid empresaId, Guid produtoFinalId, Guid? lojaId, CancellationToken ct = default)
    {
        var linhas = await context.ProdutosComposicao
            .Where(c => c.EmpresaId == empresaId && c.ProdutoFinalId == produtoFinalId && c.LojaId == lojaId)
            .ToListAsync(ct);

        if (linhas.Count > 0)
            context.ProdutosComposicao.RemoveRange(linhas);
    }
}
