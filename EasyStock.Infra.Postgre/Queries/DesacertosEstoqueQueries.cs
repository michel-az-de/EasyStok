using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Queries;

/// <summary>
/// Desacertos de estoque (S22): produtos com <c>SUM(QuantidadeDescoberta) &gt; 0</c> e as saídas dos lotes
/// descobertos desde o último ajuste do produto. <c>EmpresaId</c> no WHERE além do RLS (ADR-0010).
/// </summary>
public sealed class DesacertosEstoqueQueries(EasyStockDbContext db) : IDesacertosEstoqueQueries
{
    /// <summary>Janela das saídas lidas para montar o texto; o alerta em si não expira.</summary>
    internal const int JanelaSaidasDias = 60;
    internal const int MaximoSaidasPorProduto = 20;

    public async Task<IReadOnlyList<DesacertoProdutoLinha>> ListarAsync(Guid empresaId, Guid? lojaId, CancellationToken ct = default)
    {
        var lotes = await db.ItensEstoque
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId
                && i.Status != StatusItemEstoque.Descartado
                && (lojaId == null || i.LojaId == lojaId))
            .Where(i => i.ProdutoId != Guid.Empty
                && db.ItensEstoque.Any(o => o.EmpresaId == empresaId && o.ProdutoId == i.ProdutoId
                    && (decimal)o.QuantidadeDescoberta > 0m
                    && (lojaId == null || o.LojaId == lojaId)))
            .Select(i => new
            {
                i.Id,
                i.ProdutoId,
                Nome = i.Produto != null ? i.Produto.Nome : string.Empty,
                Atual = (decimal)i.QuantidadeAtual,
                Descoberto = (decimal)i.QuantidadeDescoberta,
            })
            .ToListAsync(ct);

        if (lotes.Count == 0) return Array.Empty<DesacertoProdutoLinha>();

        var produtoIds = lotes.Select(l => l.ProdutoId).Distinct().ToList();
        var lotesDescobertos = lotes.Where(l => l.Descoberto > 0m).Select(l => l.Id).ToList();
        var desde = DateTime.UtcNow.AddDays(-JanelaSaidasDias);

        var ultimoAjuste = await db.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId
                && produtoIds.Contains(m.ProdutoId)
                && m.Natureza == NaturezaMovimentacaoEstoque.Ajuste
                && m.DataMovimentacao >= desde)
            .GroupBy(m => m.ProdutoId)
            .Select(g => new { ProdutoId = g.Key, Em = g.Max(m => m.DataMovimentacao) })
            .ToDictionaryAsync(x => x.ProdutoId, x => x.Em, ct);

        var saidas = await db.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId
                && lotesDescobertos.Contains(m.ItemEstoqueId)
                && m.Tipo == TipoMovimentacaoEstoque.Saida
                && m.Natureza != NaturezaMovimentacaoEstoque.Ajuste
                && m.EstornadaEm == null
                && m.DataMovimentacao >= desde)
            .Select(m => new
            {
                m.ProdutoId,
                m.DataMovimentacao,
                Quantidade = (decimal)m.Quantidade,
                m.DocumentoReferencia,
            })
            .ToListAsync(ct);

        return lotes
            .GroupBy(l => l.ProdutoId)
            .Select(g =>
            {
                var corte = ultimoAjuste.TryGetValue(g.Key, out var em) ? em : DateTime.MinValue;
                var doProduto = saidas
                    .Where(s => s.ProdutoId == g.Key && s.DataMovimentacao > corte)
                    .OrderByDescending(s => s.DataMovimentacao)
                    .Take(MaximoSaidasPorProduto)
                    .Select(s => new SaidaDesacertoLinha(s.DataMovimentacao, s.Quantidade, s.DocumentoReferencia))
                    .ToList();
                return new DesacertoProdutoLinha(
                    g.Key, g.First().Nome, g.Sum(l => l.Descoberto), g.Sum(l => l.Atual), doProduto);
            })
            .Where(d => d.QuantidadeDescoberta > 0m)
            .OrderByDescending(d => d.QuantidadeDescoberta)
            .ToList();
    }
}
