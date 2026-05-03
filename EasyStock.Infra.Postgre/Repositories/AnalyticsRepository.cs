using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Defaults;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.Postgre.Repositories
{
    /// <summary>
    /// Implements aggregated analytics queries against PostgreSQL with optional Redis distributed cache (5–10 min TTL).
    /// Delegates to specialised query classes; store-intelligence methods live here.
    /// </summary>
    public sealed class AnalyticsRepository(EasyStockDbContext dbContext, IDistributedCache? cache = null)
        : IAnalyticsRepository
    {
        // ── Specialised query objects ────────────────────────────────────────

        private readonly DashboardAnalyticsQueries _dashboard = new(dbContext, cache);
        private readonly ReceitaAnalyticsQueries   _receita   = new(dbContext, cache);
        private readonly EstoqueAnalyticsQueries   _estoque   = new(dbContext, cache);

        // ── Cache helpers (used only by store-intelligence methods below) ────

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

        private static readonly TimeSpan ComparacaoTtl = TimeSpan.FromMinutes(5);

        // ── Delegation — Dashboard ───────────────────────────────────────────

        public Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
            => _dashboard.GetDashboardResumoAsync(empresaId, periodoDias, lojaId);

        public Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
            Guid empresaId, DateTime de, DateTime ate,
            TipoMovimentacaoEstoque? tipo = null, Guid? lojaId = null)
            => _dashboard.GetMovimentacoesResumoAsync(empresaId, de, ate, tipo, lojaId);

        // ── Delegation — Receita ─────────────────────────────────────────────

        public Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12, Guid? lojaId = null)
            => _receita.GetReceitaPorPeriodoAsync(empresaId, meses, lojaId);

        public Task<IReadOnlyList<MargemPorProduto>> GetMargemPorProdutoAsync(Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _receita.GetMargemPorProdutoAsync(empresaId, dias, page, pageSize, lojaId);

        public Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
            => _receita.GetVendasPorCanalAsync(empresaId, de, ate, lojaId);

        // ── Delegation — Estoque ─────────────────────────────────────────────

        public Task<(IReadOnlyList<ValidadeAlerta> Items, int TotalCount)> GetAlertasValidadeAsync(
            Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetAlertasValidadeAsync(empresaId, dias, page, pageSize, lojaId);

        public Task<(IReadOnlyList<ItemParadoDetalhe> Items, int TotalCount)> GetItensParadosDetalhadosAsync(
            Guid empresaId, int diasSemMovimento = 90, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetItensParadosDetalhadosAsync(empresaId, diasSemMovimento, page, pageSize, lojaId);

        public Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12, Guid? lojaId = null)
            => _estoque.GetSazonalidadeAsync(empresaId, produtoId, meses, lojaId);

        public Task<(IReadOnlyList<ReposicaoSugerida> Items, int TotalCount)> GetSugestaoReposicaoDetalhadaAsync(
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetSugestaoReposicaoDetalhadaAsync(empresaId, diasHistorico, page, pageSize, lojaId);

        public Task<(IReadOnlyList<ProjecaoRuptura> Items, int TotalCount)> GetProjecaoRupturaAsync(
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
            => _estoque.GetProjecaoRupturaAsync(empresaId, diasHistorico, page, pageSize, lojaId);

        // ── Store Intelligence ───────────────────────────────────────────────

        public async Task<IReadOnlyList<LojaComparacao>> GetComparacaoLojasAsync(Guid empresaId, int periodoDias = 30)
        {
            var cacheKey = $"analytics:comparacao:{empresaId}:{periodoDias}";
            var cached = await GetCachedAsync<List<LojaComparacao>>(cacheKey);
            if (cached is not null) return cached;

            var lojas = await dbContext.Lojas.AsNoTracking()
                .Where(l => l.EmpresaId == empresaId && l.Ativa)
                .Select(l => new { l.Id, l.Nome })
                .ToListAsync();

            if (lojas.Count == 0)
                return [];

            var ate = DateTime.UtcNow;
            var de = ate.AddDays(-periodoDias);
            var dias = Math.Max(1, periodoDias);
            var cutoffValidade = ate.AddDays(30);
            var cutoffParado = ate.AddDays(-90);

            // Stock aggregates grouped by LojaId
            var stockByLoja = await dbContext.ItensEstoque.AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.LojaId != null)
                .GroupBy(i => i.LojaId!.Value)
                .Select(g => new
                {
                    LojaId = g.Key,
                    TotalSkus = g.Select(i => i.ProdutoId).Distinct().Count(),
                    QuantidadeTotal = g.Sum(i => (int)i.QuantidadeAtual),
                    ValorEstoque = g.Sum(i => ((decimal?)i.PrecoVendaSugerido ?? (decimal)i.CustoUnitario * OperacionalDefaults.FallbackMargemPrecoSugerido) * (int)i.QuantidadeAtual),
                    AlertasCriticos = g.Count(i => (int)i.QuantidadeAtual <= 2),
                    ItensAbaixoMinimo = g.Count(i => (int)i.QuantidadeAtual < i.QuantidadeMinima),
                    AlertasVencimento = g.Count(i => i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoffValidade),
                    ItensParados = g.Count(i => i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado),
                    TotalItens = g.Count(),
                    ValorVencendo = g.Where(i => i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoffValidade)
                        .Sum(i => (decimal)i.CustoUnitario * (int)i.QuantidadeAtual)
                })
                .ToDictionaryAsync(x => x.LojaId);

            // Sales velocity by loja (via ItemEstoque.LojaId)
            var salesByLoja = await dbContext.MovimentacoesEstoque.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de && m.DataMovimentacao <= ate &&
                    m.ItemEstoque != null && m.ItemEstoque.LojaId != null)
                .GroupBy(m => m.ItemEstoque!.LojaId!.Value)
                .Select(g => new
                {
                    LojaId = g.Key,
                    TotalSaidas = g.Sum(m => (int)m.Quantidade),
                    ReceitaTotal = g.Sum(m => (decimal?)m.ValorTotal ?? 0m)
                })
                .ToDictionaryAsync(x => x.LojaId);

            // Company-wide average daily sales for velocity scoring
            var totalEmpresaSaidas = salesByLoja.Values.Sum(s => s.TotalSaidas);
            var mediaEmpresaDiaria = lojas.Count > 0 ? (decimal)totalEmpresaSaidas / dias / lojas.Count : 0m;

            var result = lojas.Select(loja =>
            {
                var stock = stockByLoja.GetValueOrDefault(loja.Id);
                var sales = salesByLoja.GetValueOrDefault(loja.Id);

                var totalItens = stock?.TotalItens ?? 0;
                var totalSkus = stock?.TotalSkus ?? 0;
                var quantidadeEstoque = stock?.QuantidadeTotal ?? 0;
                var valorEstoque = stock?.ValorEstoque ?? 0m;
                var alertasCriticos = stock?.AlertasCriticos ?? 0;
                var itensAbaixoMinimo = stock?.ItensAbaixoMinimo ?? 0;
                var alertasVencimento = stock?.AlertasVencimento ?? 0;
                var itensParados = stock?.ItensParados ?? 0;
                var valorVencendo = stock?.ValorVencendo ?? 0m;
                var totalSaidas = sales?.TotalSaidas ?? 0;
                var receita = sales?.ReceitaTotal ?? 0m;
                var mediaDiaria = (decimal)totalSaidas / dias;

                var alertasTotal = alertasCriticos + alertasVencimento + itensParados + itensAbaixoMinimo;
                var (score, classificacao) = CalcularHealthScore(totalItens, alertasCriticos, itensAbaixoMinimo,
                    mediaDiaria, mediaEmpresaDiaria, valorVencendo, valorEstoque, itensParados);

                return new LojaComparacao(
                    LojaId: loja.Id,
                    NomeLoja: loja.Nome,
                    HealthScore: score,
                    HealthClassificacao: classificacao,
                    ReceitaPeriodo: Math.Round(receita, 2),
                    TotalSkus: totalSkus,
                    QuantidadeEstoque: quantidadeEstoque,
                    ValorEstoque: Math.Round(valorEstoque, 2),
                    AlertasTotal: alertasTotal,
                    AlertasCriticos: alertasCriticos,
                    AlertasVencimento: alertasVencimento,
                    ItensParados: itensParados,
                    ItensAbaixoMinimo: itensAbaixoMinimo,
                    MediaVendasDiaria: Math.Round(mediaDiaria, 2));
            })
            .OrderByDescending(x => x.HealthScore)
            .ToList();

            await SetCachedAsync(cacheKey, result, ComparacaoTtl);
            return result;
        }

        public async Task<LojaResumoInteligencia?> GetResumoInteligenciaLojaAsync(Guid empresaId, Guid lojaId, int periodoDias = 30)
        {
            var cacheKey = $"analytics:resumo-loja:{empresaId}:{lojaId}:{periodoDias}";
            var cached = await GetCachedAsync<LojaResumoInteligencia>(cacheKey);
            if (cached is not null) return cached;

            var loja = await dbContext.Lojas.AsNoTracking()
                .Where(l => l.Id == lojaId && l.EmpresaId == empresaId)
                .Select(l => new { l.Id, l.Nome })
                .FirstOrDefaultAsync();
            if (loja is null) return null;

            var dashboard = await _dashboard.GetDashboardResumoAsync(empresaId, periodoDias, lojaId);

            var ate = DateTime.UtcNow;
            var de = ate.AddDays(-periodoDias);
            var dias = Math.Max(1, periodoDias);
            var cutoffValidade = ate.AddDays(30);
            var cutoffParado = ate.AddDays(-90);

            // Extra per-store metrics for health score
            var storeItems = await dbContext.ItensEstoque.AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.LojaId == lojaId)
                .Select(i => new
                {
                    IsCritical = (int)i.QuantidadeAtual <= 2,
                    IsBelowMin = (int)i.QuantidadeAtual < i.QuantidadeMinima,
                    IsExpiring = i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoffValidade,
                    IsIdle = i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado,
                    ValorVencendo = i.ValidadeEm != null && (DateTime?)i.ValidadeEm <= cutoffValidade
                        ? (decimal)i.CustoUnitario * (int)i.QuantidadeAtual : 0m
                })
                .ToListAsync();

            var totalItens = storeItems.Count;
            var alertasCriticos = storeItems.Count(i => i.IsCritical);
            var itensAbaixoMinimo = storeItems.Count(i => i.IsBelowMin);
            var itensParados = storeItems.Count(i => i.IsIdle);
            var valorVencendo = storeItems.Sum(i => i.ValorVencendo);

            // Company average for velocity scoring
            var empresaSaidas = await dbContext.MovimentacoesEstoque.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de && m.DataMovimentacao <= ate)
                .SumAsync(m => (int)m.Quantidade);
            var lojasCount = await dbContext.Lojas.AsNoTracking()
                .CountAsync(l => l.EmpresaId == empresaId && l.Ativa);
            var mediaEmpresaDiaria = lojasCount > 0 ? (decimal)empresaSaidas / dias / lojasCount : 0m;

            var (score, classificacao, dimStock, dimSales, dimExpiry, dimIdle, dimReplen) =
                CalcularHealthScoreDetalhado(totalItens, alertasCriticos, itensAbaixoMinimo,
                    dashboard.MediaVendasDiaria, mediaEmpresaDiaria,
                    valorVencendo, dashboard.ValorTotalEstoque, itensParados);

            // Last movement
            var ultimaMov = await dbContext.MovimentacoesEstoque.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId && m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId)
                .OrderByDescending(m => m.DataMovimentacao)
                .Select(m => (DateTime?)m.DataMovimentacao)
                .FirstOrDefaultAsync();

            var result = new LojaResumoInteligencia(
                LojaId: loja.Id,
                NomeLoja: loja.Nome,
                TotalSkus: dashboard.TotalSkus,
                QuantidadeTotalEmEstoque: dashboard.QuantidadeTotalEmEstoque,
                ValorTotalEstoque: dashboard.ValorTotalEstoque,
                ValorCustoEstoque: dashboard.ValorCustoEstoque,
                AlertasEstoqueBaixo: dashboard.AlertasEstoqueBaixo,
                AlertasVencimento: dashboard.AlertasVencimento,
                AlertasItensParados: dashboard.AlertasItensParados,
                ItensAbaixoMinimo: itensAbaixoMinimo,
                MediaVendasDiaria: dashboard.MediaVendasDiaria,
                ReceitaPeriodo: dashboard.ReceitaEstimadaPeriodo,
                UltimaMovimentacao: ultimaMov,
                HealthScore: score,
                HealthClassificacao: classificacao,
                DimStockHealth: dimStock,
                DimSalesVelocity: dimSales,
                DimExpiryRisk: dimExpiry,
                DimIdleRisk: dimIdle,
                DimReplenishmentUrgency: dimReplen);

            await SetCachedAsync(cacheKey, result, ComparacaoTtl);
            return result;
        }

        public async Task<IReadOnlyList<ProdutoTurnover>> GetTopProdutosPorLojaAsync(
            Guid empresaId, Guid lojaId, int periodoDias = 30, int top = 10, bool ascending = false)
        {
            var cacheKey = $"analytics:turnover:{empresaId}:{lojaId}:{periodoDias}:{top}:{ascending}";
            var cached = await GetCachedAsync<List<ProdutoTurnover>>(cacheKey);
            if (cached is not null) return cached;

            var de = DateTime.UtcNow.AddDays(-periodoDias);
            var ate = DateTime.UtcNow;
            var dias = Math.Max(1, periodoDias);

            var raw = await dbContext.MovimentacoesEstoque.AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de && m.DataMovimentacao <= ate &&
                    m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId)
                .Join(dbContext.Produtos.AsNoTracking(), m => m.ProdutoId, p => p.Id, (m, p) => new
                {
                    m.ProdutoId,
                    NomeProduto = p.Nome,
                    Quantidade = (int)m.Quantidade,
                    Valor = (decimal?)m.ValorTotal ?? 0m
                })
                .ToListAsync();

            var grouped = raw
                .GroupBy(x => new { x.ProdutoId, x.NomeProduto })
                .Select(g => new ProdutoTurnover(
                    ProdutoId: g.Key.ProdutoId,
                    NomeProduto: g.Key.NomeProduto,
                    QuantidadeVendida: g.Sum(x => x.Quantidade),
                    ReceitaGerada: Math.Round(g.Sum(x => x.Valor), 2),
                    TaxaSaidaDiaria: Math.Round((decimal)g.Sum(x => x.Quantidade) / dias, 2)));

            var result = (ascending
                ? grouped.OrderBy(x => x.QuantidadeVendida)
                : grouped.OrderByDescending(x => x.QuantidadeVendida))
                .Take(top)
                .ToList();

            await SetCachedAsync(cacheKey, result, ComparacaoTtl);
            return result;
        }

        public async Task<IReadOnlyList<IndicadorAcao>> GetIndicadoresAcaoAsync(
            Guid empresaId, int periodoDias = 30, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:indicadores:{empresaId}:{periodoDias}:{lojaId}";
            var cached = await GetCachedAsync<List<IndicadorAcao>>(cacheKey);
            if (cached is not null) return cached;

            var comparacao = await GetComparacaoLojasAsync(empresaId, periodoDias);
            var lojasAlvo = lojaId.HasValue
                ? comparacao.Where(l => l.LojaId == lojaId.Value).ToList()
                : comparacao.ToList();

            var mediaHealth = comparacao.Count > 0 ? comparacao.Average(l => l.HealthScore) : 0m;
            var indicadores = new List<IndicadorAcao>();

            foreach (var loja in lojasAlvo)
            {
                // Health critico
                if (loja.HealthScore < 40)
                    indicadores.Add(new IndicadorAcao("atencao_imediata", "critico",
                        $"{loja.NomeLoja}: atenção imediata",
                        $"Score de saúde {loja.HealthScore:F0}/100 — abaixo do limiar crítico.",
                        loja.LojaId, loja.NomeLoja, null));

                // Alertas criticos
                if (loja.AlertasCriticos > 0)
                    indicadores.Add(new IndicadorAcao("alto_risco", "alto",
                        $"{loja.NomeLoja}: {loja.AlertasCriticos} item(ns) em estoque crítico",
                        "Itens com 2 unidades ou menos. Risco de ruptura iminente.",
                        loja.LojaId, loja.NomeLoja, null));

                // Bom desempenho
                if (loja.HealthScore >= 80)
                    indicadores.Add(new IndicadorAcao("bom_desempenho", "positivo",
                        $"{loja.NomeLoja}: excelente saúde operacional",
                        $"Score {loja.HealthScore:F0}/100 — operação saudável.",
                        loja.LojaId, loja.NomeLoja, null));

                // Itens parados
                if (loja.ItensParados > 0 && loja.QuantidadeEstoque > 0 &&
                    (decimal)loja.ItensParados / loja.QuantidadeEstoque > 0.3m)
                    indicadores.Add(new IndicadorAcao("excesso_ociosidade", "medio",
                        $"{loja.NomeLoja}: {loja.ItensParados} item(ns) parado(s)",
                        "Mais de 30% do estoque sem movimentação há 90+ dias.",
                        loja.LojaId, loja.NomeLoja, null));
                else if (loja.ItensParados > 0)
                    indicadores.Add(new IndicadorAcao("excesso_ociosidade", "baixo",
                        $"{loja.NomeLoja}: {loja.ItensParados} item(ns) parado(s)",
                        "Itens sem movimentação há 90+ dias.",
                        loja.LojaId, loja.NomeLoja, null));

                // Reposicao urgente
                if (loja.ItensAbaixoMinimo > 3)
                    indicadores.Add(new IndicadorAcao("urgencia_reposicao", "alto",
                        $"{loja.NomeLoja}: {loja.ItensAbaixoMinimo} itens abaixo do mínimo",
                        "Vários itens precisam de reposição urgente.",
                        loja.LojaId, loja.NomeLoja, null));
                else if (loja.ItensAbaixoMinimo > 0)
                    indicadores.Add(new IndicadorAcao("urgencia_reposicao", "medio",
                        $"{loja.NomeLoja}: {loja.ItensAbaixoMinimo} item(ns) abaixo do mínimo",
                        "Itens precisam de reposição.",
                        loja.LojaId, loja.NomeLoja, null));

                // Risco de vencimento
                if (loja.AlertasVencimento > 0)
                    indicadores.Add(new IndicadorAcao("risco_vencimento", "alto",
                        $"{loja.NomeLoja}: {loja.AlertasVencimento} item(ns) próximo(s) do vencimento",
                        "Itens com validade nos próximos 30 dias.",
                        loja.LojaId, loja.NomeLoja, null));

                // Oportunidade comercial
                if (mediaHealth > 0 && loja.MediaVendasDiaria > 0 && loja.HealthScore >= 60)
                    indicadores.Add(new IndicadorAcao("oportunidade", "positivo",
                        $"{loja.NomeLoja}: boa velocidade de vendas",
                        $"Média de {loja.MediaVendasDiaria:F1} un/dia. Considere ampliar mix.",
                        loja.LojaId, loja.NomeLoja, null));
            }

            // Sort: critico first, then alto, medio, baixo, positivo
            var severityOrder = new Dictionary<string, int>
            {
                ["critico"] = 0, ["alto"] = 1, ["medio"] = 2, ["baixo"] = 3, ["positivo"] = 4
            };
            var result = indicadores
                .OrderBy(i => severityOrder.GetValueOrDefault(i.Severidade, 5))
                .ThenBy(i => i.NomeLoja)
                .ToList();

            await SetCachedAsync(cacheKey, result, ComparacaoTtl);
            return result;
        }

        // ── Health Score Calculation ─────────────────────────────────────────

        private static (decimal Score, string Classificacao) CalcularHealthScore(
            int totalItens, int itensCriticos, int itensAbaixoMinimo,
            decimal mediaDiaria, decimal mediaEmpresaDiaria,
            decimal valorVencendo, decimal valorEstoque, int itensParados)
        {
            var (score, classificacao, _, _, _, _, _) = CalcularHealthScoreDetalhado(
                totalItens, itensCriticos, itensAbaixoMinimo,
                mediaDiaria, mediaEmpresaDiaria, valorVencendo, valorEstoque, itensParados);
            return (score, classificacao);
        }

        private static (decimal Score, string Classificacao,
            decimal DimStock, decimal DimSales, decimal DimExpiry, decimal DimIdle, decimal DimReplen)
            CalcularHealthScoreDetalhado(
                int totalItens, int itensCriticos, int itensAbaixoMinimo,
                decimal mediaDiaria, decimal mediaEmpresaDiaria,
                decimal valorVencendo, decimal valorEstoque, int itensParados)
        {
            var maxItens = Math.Max(totalItens, 1);

            // StockHealth (30%): fewer critical items = higher score
            var dimStock = Math.Max(0, 100m - (decimal)itensCriticos / maxItens * 100m);

            // SalesVelocity (25%): store rate vs company average
            decimal dimSales;
            if (mediaEmpresaDiaria <= 0)
                dimSales = mediaDiaria > 0 ? 100m : 50m;
            else
                dimSales = Math.Min(100m, mediaDiaria / mediaEmpresaDiaria * 100m);

            // ExpiryRisk (20%): less value at expiry risk = higher score
            var maxValor = Math.Max(valorEstoque, 1m);
            var dimExpiry = Math.Max(0, 100m - valorVencendo / maxValor * 100m);

            // IdleRisk (15%): fewer idle items = higher score
            var dimIdle = Math.Max(0, 100m - (decimal)itensParados / maxItens * 100m);

            // ReplenishmentUrgency (10%): fewer items below minimum = higher score
            var dimReplen = Math.Max(0, 100m - (decimal)itensAbaixoMinimo / maxItens * 100m);

            var score = Math.Round(
                0.30m * dimStock +
                0.25m * dimSales +
                0.20m * dimExpiry +
                0.15m * dimIdle +
                0.10m * dimReplen, 1);

            var classificacao = score switch
            {
                >= 80 => "Excelente",
                >= 60 => "Bom",
                >= 40 => "Atencao",
                _ => "Critico"
            };

            return (score, classificacao,
                Math.Round(dimStock, 1), Math.Round(dimSales, 1),
                Math.Round(dimExpiry, 1), Math.Round(dimIdle, 1), Math.Round(dimReplen, 1));
        }
    }
}
