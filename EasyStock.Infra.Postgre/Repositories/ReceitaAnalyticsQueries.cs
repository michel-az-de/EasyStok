using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>Consultas de analytics para receita, margem e vendas por canal.</summary>
internal sealed class ReceitaAnalyticsQueries(EasyStockDbContext dbContext, IDistributedCache? cache = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private async Task<T?> GetCachedAsync<T>(string key)
    {
        if (cache is null) return default;
        var raw = await cache.GetStringAsync(key);
        return string.IsNullOrEmpty(raw) ? default : JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private async Task SetCachedAsync<T>(string key, T value, TimeSpan ttl)
    {
        if (cache is null) return;
        var serialized = JsonSerializer.Serialize(value, JsonOptions);
        await cache.SetStringAsync(key, serialized, new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });
    }

    private static readonly TimeSpan ReceitaTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MargemTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan CanalTtl = TimeSpan.FromMinutes(10);

    public async Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:receita:{empresaId}:{meses}:{lojaId}";
        var cached = await GetCachedAsync<List<ReceitaPorPeriodo>>(cacheKey);
        if (cached is not null) return cached;

        var de = DateTime.SpecifyKind(DateTime.UtcNow.AddMonths(-meses), DateTimeKind.Utc);

        var vendasQuery = dbContext.Vendas
            .AsNoTracking()
            .Where(v => v.EmpresaId == empresaId && v.DataVenda >= de);
        if (lojaId.HasValue)
            vendasQuery = vendasQuery.Where(v => v.LojaId == lojaId.Value);

        var raw = await vendasQuery
            .Select(v => new
            {
                v.DataVenda.Year,
                v.DataVenda.Month,
                ValorTotal = (decimal)v.ValorTotal,
                TotalItens = v.ItensVenda != null ? v.ItensVenda.Sum(i => (int)i.Quantidade) : 0
            })
            .ToListAsync();

        var result = raw
            .GroupBy(v => new { v.Year, v.Month })
            .Select(g =>
            {
                var totalVendas = g.Count();
                var receita = g.Sum(v => v.ValorTotal);
                var totalItens = g.Sum(v => v.TotalItens);
                return new ReceitaPorPeriodo(
                    Ano: g.Key.Year,
                    Mes: g.Key.Month,
                    ReceitaBruta: Math.Round(receita, 2),
                    TotalVendas: totalVendas,
                    TotalItensVendidos: totalItens,
                    TicketMedio: totalVendas > 0 ? Math.Round(receita / totalVendas, 2) : 0m);
            })
            .OrderBy(x => x.Ano).ThenBy(x => x.Mes)
            .ToList();

        await SetCachedAsync(cacheKey, result, ReceitaTtl);
        return result;
    }

    public async Task<IReadOnlyList<MargemPorProduto>> GetMargemPorProdutoAsync(Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:margem:{empresaId}:{dias}:{page}:{pageSize}:{lojaId}";
        var cached = await GetCachedAsync<List<MargemPorProduto>>(cacheKey);
        if (cached is not null) return cached;

        var de = DateTime.UtcNow.AddDays(-dias);

        var movQuery = dbContext.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.Tipo == TipoMovimentacaoEstoque.Saida &&
                m.DataMovimentacao >= de &&
                m.ValorUnitario != null);
        if (lojaId.HasValue)
            movQuery = movQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var raw = await movQuery
            .Join(dbContext.Produtos.AsNoTracking(),
                m => m.ProdutoId,
                p => p.Id,
                (m, p) => new { m, p })
            .GroupJoin(dbContext.ItensEstoque.AsNoTracking(),
                x => x.m.ItemEstoqueId,
                ie => ie.Id,
                (x, ies) => new { x.m, x.p, ies })
            .SelectMany(
                x => x.ies.DefaultIfEmpty(),
                (x, ie) => new
                {
                    x.m.ProdutoId,
                    NomeProduto = x.p.Nome,
                    CustoUnitario = ie != null ? (decimal)ie.CustoUnitario : (x.p.CustoReferencia != null ? (decimal)x.p.CustoReferencia : 0m),
                    PrecoVenda = (decimal)x.m.ValorUnitario!,
                    Quantidade = (int)x.m.Quantidade
                })
            .ToListAsync();

        var result = raw
            .GroupBy(x => new { x.ProdutoId, x.NomeProduto })
            .Select(g =>
            {
                var custoMedio = g.Average(x => x.CustoUnitario);
                var precoMedio = g.Average(x => x.PrecoVenda);
                var qtdVendida = g.Sum(x => x.Quantidade);
                var margemAbs = precoMedio - custoMedio;
                var margemPct = custoMedio > 0 ? margemAbs / custoMedio * 100m : 0m;
                return new MargemPorProduto(
                    ProdutoId: g.Key.ProdutoId,
                    NomeProduto: g.Key.NomeProduto,
                    CustoMedio: Math.Round(custoMedio, 2),
                    PrecoMedioVenda: Math.Round(precoMedio, 2),
                    MargemAbsoluta: Math.Round(margemAbs, 2),
                    MargemPercentual: Math.Round(margemPct, 2),
                    QuantidadeVendida: qtdVendida);
            })
            .OrderByDescending(x => x.MargemPercentual)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        await SetCachedAsync(cacheKey, result, MargemTtl);
        return result;
    }

    public async Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:canal:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{lojaId}";
        var cached = await GetCachedAsync<List<VendaPorCanal>>(cacheKey);
        if (cached is not null) return cached;

        var vendasQuery = dbContext.Vendas
            .AsNoTracking()
            .Where(v => v.EmpresaId == empresaId &&
                v.DataVenda >= DateTime.SpecifyKind(de, DateTimeKind.Utc) &&
                v.DataVenda <= DateTime.SpecifyKind(ate, DateTimeKind.Utc));
        if (lojaId.HasValue)
            vendasQuery = vendasQuery.Where(v => v.LojaId == lojaId.Value);

        var raw = await vendasQuery
            .Select(v => new
            {
                v.Canal,
                ValorTotal = (decimal)v.ValorTotal,
                TotalItens = v.ItensVenda != null ? v.ItensVenda.Sum(i => (int)i.Quantidade) : 0
            })
            .ToListAsync();

        var totalReceita = raw.Sum(v => v.ValorTotal);

        var result = raw
            .GroupBy(v => v.Canal)
            .Select(g =>
            {
                var totalVendas = g.Count();
                var receita = g.Sum(v => v.ValorTotal);
                var totalItens = g.Sum(v => v.TotalItens);
                return new VendaPorCanal(
                    Canal: g.Key,
                    TotalVendas: totalVendas,
                    TotalItensVendidos: totalItens,
                    ReceitaTotal: Math.Round(receita, 2),
                    TicketMedio: totalVendas > 0 ? Math.Round(receita / totalVendas, 2) : 0m,
                    PercentualReceita: totalReceita > 0 ? Math.Round(receita / totalReceita * 100m, 2) : 0m);
            })
            .OrderByDescending(x => x.ReceitaTotal)
            .ToList();

        await SetCachedAsync(cacheKey, result, CanalTtl);
        return result;
    }
}
