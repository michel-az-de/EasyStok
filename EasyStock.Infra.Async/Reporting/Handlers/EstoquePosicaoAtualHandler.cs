using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.EstoquePosicaoAtual;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers;

/// <summary>
/// Handler do relatório "Posição de estoque" (Tenant).
/// Usa <see cref="ITenantScopedQueryBuilder"/> para isolamento explícito por empresa (ADR-R07).
/// Streaming via <see cref="IAsyncEnumerable{T}"/> — sem materializar toda a coleção.
/// Nota: CustoUnitario = custo da última entrada; não é Custo Médio Acumulado (CMA).
/// </summary>
public sealed class EstoquePosicaoAtualHandler(
    EasyStockDbContext db,
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<EstoquePosicaoAtualParams, EstoquePosicaoAtualRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(EstoquePosicaoAtualParams parametros)
    {
        return new ReportSchema(
            title: "Posição de estoque",
            fileNameBase: $"estoque-posicao_{DateOnly.FromDateTime(DateTime.Today):yyyy-MM-dd}",
            columns:
            [
                new("Sku",               "SKU",                                        0),
                new("Nome",              "Produto",                                    1),
                new("Categoria",         "Categoria",                                  2),
                new("LojaNome",          "Loja",                                       3),
                new("QtdAtual",          "Qtd. atual",                                 4, "0.###"),
                new("CustoUnitario",     "Custo unitário (última entrada — não é CMA)", 5, "0.00"),
                new("ValorEstoque",      "Valor em estoque (R$)",                      6, "0.00"),
                new("UltimaMovimentacao","Última movimentação",                        7),
            ]);
    }

    public Task ValidateAsync(EstoquePosicaoAtualParams parametros, CancellationToken ct) =>
        Task.CompletedTask; // Sem restrições de período; snapshot = agora

    public async IAsyncEnumerable<EstoquePosicaoAtualRow> StreamAsync(
        EstoquePosicaoAtualParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // ADR-R07: query explícita com WHERE EmpresaId via ITenantScopedQueryBuilder
        var query = tenantQuery.Query<EasyStock.Domain.Entities.ItemEstoque>()
            .AsNoTracking();

        if (!parametros.IncluirSemEstoque)
            query = query.Where(i => i.QuantidadeAtual.Value > 0);

        if (parametros.LojaId.HasValue)
            query = query.Where(i => i.LojaId == parametros.LojaId.Value);

        var projected = query
            .Select(i => new
            {
                i.ProdutoId,
                i.ProdutoVariacaoId,
                i.LojaId,
                QtdAtual = i.QuantidadeAtual.Value,
                CustoUnitario = i.CustoUnitario.Valor,
                UltimaMovimentacaoEm = i.UltimaMovimentacaoEm,

                // Produto
                // SkuBase eh CodigoSku? (nullable VO). Conditional p/ alinhar
                // tipo do anonimo a string? — downstream (linha do "??") ja trata null.
                ProdutoSkuBase = db.Produtos
                    .Where(p => p.Id == i.ProdutoId)
                    .Select(p => p.SkuBase != null ? p.SkuBase.Value : null)
                    .FirstOrDefault(),
                ProdutoNome = db.Produtos
                    .Where(p => p.Id == i.ProdutoId)
                    .Select(p => p.Nome)
                    .FirstOrDefault(),
                CategoriaId = db.Produtos
                    .Where(p => p.Id == i.ProdutoId)
                    .Select(p => p.CategoriaId)
                    .FirstOrDefault(),

                // Variação (se existir)
                VariacaoSku = i.ProdutoVariacaoId == null ? null
                    : db.ProdutosVariacao
                        .Where(v => v.Id == i.ProdutoVariacaoId)
                        // Sku eh CodigoSku? — conditional preserva null (downstream "??")
                        .Select(v => v.Sku != null ? v.Sku.Value : null)
                        .FirstOrDefault(),
                VariacaoNome = i.ProdutoVariacaoId == null ? null
                    : db.ProdutosVariacao
                        .Where(v => v.Id == i.ProdutoVariacaoId)
                        .Select(v => v.Nome)
                        .FirstOrDefault(),

                // Loja (opcional)
                LojaNome = i.LojaId == null ? null
                    : db.Lojas
                        .Where(l => l.Id == i.LojaId)
                        .Select(l => l.Nome)
                        .FirstOrDefault(),
            })
            .AsAsyncEnumerable();

        await foreach (var r in projected.WithCancellation(ct))
        {
            // Filtrar por CategoriaId se informado
            if (parametros.CategoriaId.HasValue && r.CategoriaId != parametros.CategoriaId.Value)
                continue;

            var categoriaNome = r.CategoriaId == Guid.Empty ? "Sem categoria"
                : await db.Categorias
                    .Where(c => c.Id == r.CategoriaId)
                    .Select(c => c.Nome)
                    .FirstOrDefaultAsync(ct) ?? "Sem categoria";

            // SKU: variação prevalece sobre o produto base
            var sku = r.VariacaoSku ?? r.ProdutoSkuBase ?? "-";

            // Nome: Produto + "(Variação)" se aplicável
            var nome = r.VariacaoNome is { Length: > 0 }
                ? $"{r.ProdutoNome} — {r.VariacaoNome}"
                : r.ProdutoNome ?? "-";

            var valor = r.QtdAtual * r.CustoUnitario;

            var ultimaMov = r.UltimaMovimentacaoEm.HasValue
                ? r.UltimaMovimentacaoEm.Value.ToString("dd/MM/yyyy")
                : null;

            yield return new EstoquePosicaoAtualRow(
                Sku: sku,
                Nome: nome,
                Categoria: categoriaNome,
                LojaNome: r.LojaNome,
                QtdAtual: r.QtdAtual,
                CustoUnitario: r.CustoUnitario,
                ValorEstoque: valor,
                UltimaMovimentacao: ultimaMov);
        }
    }

    public EstoquePosicaoAtualParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<EstoquePosicaoAtualParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar EstoquePosicaoAtualParams.");
}
