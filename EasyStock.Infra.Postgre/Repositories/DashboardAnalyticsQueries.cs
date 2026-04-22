using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>Consultas de analytics para dashboard e movimentações.</summary>
internal sealed class DashboardAnalyticsQueries(EasyStockDbContext dbContext, IDistributedCache? cache = null)
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

    private static readonly TimeSpan DashboardTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MovimentacaoTtl = TimeSpan.FromMinutes(5);

    public async Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
    {
        var cacheKey = $"analytics:dashboard:{empresaId}:{periodoDias}:{lojaId}";
        var cached = await GetCachedAsync<DashboardResumo>(cacheKey);
        if (cached is not null) return cached;

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-periodoDias);

        // Estoque
        var estoqueQuery = dbContext.ItensEstoque
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && i.Status != StatusItemEstoque.Vencido);
        if (lojaId.HasValue)
            estoqueQuery = estoqueQuery.Where(i => i.LojaId == lojaId.Value);

        var estoqueData = await estoqueQuery.Select(i => new
        {
            Quantidade = (int)i.QuantidadeAtual,
            ValorCusto = (decimal)i.CustoUnitario * (int)i.QuantidadeAtual,
            ValorVenda = ((decimal?)i.PrecoVendaSugerido ?? (decimal)i.CustoUnitario * 1.3m) * (int)i.QuantidadeAtual,
            EstaAbaixoMinimo = (int)i.QuantidadeAtual < i.QuantidadeMinima
        }).ToListAsync();

        var totalSkus = await estoqueQuery.Select(i => i.ProdutoId).Distinct().CountAsync();
        var totalQtd = estoqueData.Sum(e => e.Quantidade);
        var valorCusto = estoqueData.Sum(e => e.ValorCusto);
        var valorVenda = estoqueData.Sum(e => e.ValorVenda);
        var alertasBaixo = estoqueData.Count(e => e.EstaAbaixoMinimo);

        // Alertas de validade (30 dias)
        var cutoffValidade = DateTime.UtcNow.AddDays(30);
        var validadeQuery = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoffValidade);
        if (lojaId.HasValue)
            validadeQuery = validadeQuery.Where(i => i.LojaId == lojaId.Value);
        var alertasValidade = await validadeQuery.CountAsync();

        // Alertas de itens parados (30 dias sem movimento)
        var cutoffParado = DateTime.UtcNow.AddDays(-30);
        var paradosQuery = dbContext.ItensEstoque.AsNoTracking()
            .Where(i => i.EmpresaId == empresaId &&
                        (int)i.QuantidadeAtual > 0 &&
                        (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado));
        if (lojaId.HasValue)
            paradosQuery = paradosQuery.Where(i => i.LojaId == lojaId.Value);
        var alertasParados = await paradosQuery.CountAsync();

        // Vendas do período (via movimentações de saída — mesma lógica do original)
        var movQuery = dbContext.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.Tipo == TipoMovimentacaoEstoque.Saida &&
                m.DataMovimentacao >= de &&
                m.DataMovimentacao <= ate);
        if (lojaId.HasValue)
            movQuery = movQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var movData = await movQuery
            .Select(m => new { Quantidade = (int)m.Quantidade, ValorTotal = (decimal?)m.ValorTotal ?? 0m })
            .ToListAsync();

        var totalSaidasQtd = movData.Sum(m => m.Quantidade);
        var receitaEstimada = movData.Sum(m => m.ValorTotal);
        var dias = Math.Max(1, periodoDias);
        var mediaVendasDiaria = (decimal)totalSaidasQtd / dias;

        var result = new DashboardResumo(
            EmpresaId: empresaId,
            Periodo: periodoDias,
            TotalSkus: totalSkus,
            QuantidadeTotalEmEstoque: totalQtd,
            ValorTotalEstoque: Math.Round(valorVenda, 2),
            ValorCustoEstoque: Math.Round(valorCusto, 2),
            MediaVendasDiaria: Math.Round(mediaVendasDiaria, 2),
            ProjecaoVendasPeriodo: Math.Round(mediaVendasDiaria * periodoDias, 0),
            ReceitaEstimadaPeriodo: Math.Round(receitaEstimada, 2),
            AlertasEstoqueBaixo: alertasBaixo,
            AlertasVencimento: alertasValidade,
            AlertasItensParados: alertasParados);

        await SetCachedAsync(cacheKey, result, DashboardTtl);
        return result;
    }

    public async Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
        Guid empresaId,
        DateTime de,
        DateTime ate,
        TipoMovimentacaoEstoque? tipo = null,
        Guid? lojaId = null)
    {
        var cacheKey = $"analytics:movimentacoes:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{tipo}:{lojaId}";
        var cached = await GetCachedAsync<List<MovimentacaoResumo>>(cacheKey);
        if (cached is not null) return cached;

        var query = dbContext.MovimentacoesEstoque
            .AsNoTracking()
            .Where(m => m.EmpresaId == empresaId &&
                m.DataMovimentacao >= de &&
                m.DataMovimentacao <= ate);

        if (tipo.HasValue)
            query = query.Where(m => m.Tipo == tipo.Value);
        if (lojaId.HasValue)
            query = query.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

        var raw = await query
            .Select(m => new
            {
                m.DataMovimentacao.Year,
                m.DataMovimentacao.Month,
                m.DataMovimentacao.Day,
                m.Tipo,
                Quantidade = (int)m.Quantidade,
                Valor = (decimal?)m.ValorTotal ?? 0m
            })
            .ToListAsync();

        var result = raw
            .GroupBy(m => new { m.Year, m.Month, m.Day, m.Tipo })
            .Select(g => new MovimentacaoResumo(
                Ano: g.Key.Year,
                Mes: g.Key.Month,
                Dia: g.Key.Day,
                Tipo: g.Key.Tipo,
                TotalMovimentacoes: g.Count(),
                QuantidadeTotal: g.Sum(m => m.Quantidade),
                ValorTotal: Math.Round(g.Sum(m => m.Valor), 2)))
            .OrderBy(x => x.Ano).ThenBy(x => x.Mes).ThenBy(x => x.Dia).ThenBy(x => x.Tipo)
            .ToList();

        await SetCachedAsync(cacheKey, result, MovimentacaoTtl);
        return result;
    }
}
