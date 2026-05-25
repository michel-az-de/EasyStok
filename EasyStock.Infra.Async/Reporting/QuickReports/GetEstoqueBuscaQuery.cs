using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.QuickReports;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.QuickReports;

/// <summary>
/// Quick Report: estoque-busca — localiza um produto por SKU, código interno
/// ou fragmento de nome e retorna a posição de estoque atual.
/// Síncrono, &lt; 1s, sem paginação (§27.7). Retorna o item mais relevante
/// (primeiro match, ordenado por quantidade atual desc para priorizar itens com saldo).
/// </summary>
public sealed class GetEstoqueBuscaQuery(
    EasyStockDbContext db,
    ICurrentUserAccessor currentUser)
{
    public async Task<EstoqueBuscaDto?> ExecuteAsync(
        string busca,
        Guid? lojaId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(busca))
            return null;

        var empresaId = currentUser.EmpresaId;
        var termoBusca = busca.Trim().ToLowerInvariant();

        var query = db.ItensEstoque
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId
                     && i.ChavePesquisa != null
                     && i.ChavePesquisa.ToLower().Contains(termoBusca));

        if (lojaId.HasValue)
            query = query.Where(i => i.LojaId == lojaId.Value);

        var item = await query
            .OrderByDescending(i => i.QuantidadeAtual.Value)
            .Select(i => new
            {
                i.Id,
                SkuBase = i.Produto != null ? i.Produto.SkuBase!.Value : (string?)null,
                SkuVariacao = i.ProdutoVariacao != null ? i.ProdutoVariacao.Sku!.Value : (string?)null,
                NomeProduto = i.Produto != null ? i.Produto.Nome : "—",
                Variacao = i.VariacaoDescricao,
                LojaNome = i.Loja != null ? i.Loja.Nome : null,
                QtdAtual = i.QuantidadeAtual.Value,
                CustoUnit = i.CustoUnitario.Valor,
                Status = i.Status.ToString(),
            })
            .FirstOrDefaultAsync(ct);

        if (item is null)
            return null;

        var sku = item.SkuVariacao ?? item.SkuBase ?? "—";
        var valorEstoque = Math.Round(item.QtdAtual * item.CustoUnit, 2);

        return new EstoqueBuscaDto(
            ItemEstoqueId: item.Id,
            Sku: sku,
            Nome: item.NomeProduto,
            Variacao: item.Variacao,
            LojaNome: item.LojaNome,
            QtdAtual: item.QtdAtual,
            CustoUnitario: item.CustoUnit,
            ValorEstoque: valorEstoque,
            StatusEstoque: item.Status);
    }
}
