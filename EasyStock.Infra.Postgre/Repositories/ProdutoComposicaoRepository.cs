using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

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

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>> GetByProdutosFinaisAsync(
        Guid empresaId, IReadOnlyList<Guid> produtoFinaisIds, Guid? lojaId, CancellationToken ct = default)
    {
        if (produtoFinaisIds.Count == 0)
            return new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>(0);

        // 1 query: traz override + padrao no mesmo round trip. Resolucao override-vs-padrao
        // por produto e feita em memoria (pequeno N, evita 2 round trips).
        var todas = await context.ProdutosComposicao
            .AsNoTracking()
            .Include(c => c.ProdutoFinal)
            .Include(c => c.Insumo)
            .Where(c => c.EmpresaId == empresaId
                     && produtoFinaisIds.Contains(c.ProdutoFinalId)
                     && (c.LojaId == null || (lojaId.HasValue && c.LojaId == lojaId.Value)))
            .OrderBy(c => c.OrdemExibicao)
            .ToListAsync(ct);

        var resultado = new Dictionary<Guid, IReadOnlyCollection<ProdutoComposicao>>(produtoFinaisIds.Count);
        foreach (var grupo in todas.GroupBy(c => c.ProdutoFinalId))
        {
            if (lojaId.HasValue)
            {
                var override_ = grupo.Where(c => c.LojaId == lojaId.Value).ToList();
                if (override_.Count > 0)
                {
                    resultado[grupo.Key] = override_;
                    continue;
                }
            }
            var padrao = grupo.Where(c => c.LojaId == null).ToList();
            if (padrao.Count > 0)
                resultado[grupo.Key] = padrao;
        }
        return resultado;
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

    public async Task<IReadOnlyList<Produto>> BuscarProdutosFinaisAsync(
        Guid empresaId, string? termo, int limit, Guid? lojaId, CancellationToken ct = default)
    {
        // Sub-query: ProdutoIds que tem pelo menos 1 composicao no escopo (LojaId override ou padrao null).
        // Se lojaId informado, prefere produtos com override; mas tambem inclui os que so tem receita padrao.
        var idsComReceita = context.ProdutosComposicao
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && (c.LojaId == lojaId || c.LojaId == null))
            .Select(c => c.ProdutoFinalId)
            .Distinct();

        var query = context.Produtos
            .AsNoTracking()
            .Where(p => p.EmpresaId == empresaId && idsComReceita.Contains(p.Id));

        if (!string.IsNullOrWhiteSpace(termo))
        {
            var pattern = $"%{termo.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.Nome, pattern));
        }

        return await query
            .OrderBy(p => p.Nome)
            .Take(Math.Clamp(limit, 1, 50))
            .ToListAsync(ct);
    }
}
