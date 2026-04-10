using EasyStock.Application.Ports.Output.Persistence;
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
    /// </summary>
    public sealed class AnalyticsRepository(EasyStockDbContext dbContext, IDistributedCache? cache = null)
        : IAnalyticsRepository
    {
        // Cache helpers

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly TimeSpan DashboardTtl    = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ReceitaTtl      = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MargemTtl       = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MovimentacaoTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ValidadeTtl     = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ParadosTtl      = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SazonalidadeTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ReposicaoTtl    = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ProjecaoTtl     = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CanalTtl        = TimeSpan.FromMinutes(10);

        // Dashboard alert thresholds (independent of per-store configuration)
        private const int DashboardDiasAlertaVencimento = 30;
        private const int DashboardDiasAlertaParado     = 90;

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

        private static (DateTime De, DateTime Ate) BuildPeriodo(int dias)
        {
            var ate = DateTime.UtcNow;
            return (ate.AddDays(-dias), ate);
        }

        private async Task<Dictionary<Guid, decimal>> GetTaxasSaidaAsync(
            Guid empresaId, DateTime de, DateTime ate, int dias)
        {
            return await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de &&
                    m.DataMovimentacao <= ate)
                .GroupBy(m => m.ProdutoId)
                .Select(g => new { ProdutoId = g.Key, Total = g.Sum(m => m.Quantidade.Value) })
                .ToDictionaryAsync(x => x.ProdutoId, x => (decimal)x.Total / dias);
        }

        // Dashboard

        public async Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30)
        {
            var cacheKey = $"analytics:dashboard:{empresaId}:{periodoDias}";
            var cached = await GetCachedAsync<DashboardResumo>(cacheKey);
            if (cached is not null) return cached;

            var (de, ate) = BuildPeriodo(periodoDias);

            // Estoque
            var estoqueQuery = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId);

            var estoqueData = await estoqueQuery.Select(i => new
            {
                Quantidade = i.QuantidadeAtual.Value,
                ValorCusto = i.CustoUnitario.Valor * i.QuantidadeAtual.Value,
                ValorVenda = (i.PrecoVendaSugerido != null
                    ? i.PrecoVendaSugerido.Valor
                    : i.CustoUnitario.Valor * 1.3m) * i.QuantidadeAtual.Value,
                EstaAbaixoMinimo = i.QuantidadeAtual.Value < i.QuantidadeMinima
            }).ToListAsync();

            var totalSkus = await estoqueQuery.Select(i => i.ProdutoId).Distinct().CountAsync();
            var totalQtd = estoqueData.Sum(e => e.Quantidade);
            var valorCusto = estoqueData.Sum(e => e.ValorCusto);
            var valorVenda = estoqueData.Sum(e => e.ValorVenda);
            var alertasEstoqueBaixo = estoqueData.Count(e => e.EstaAbaixoMinimo);

            // Alertas vencimento
            var cutoffValidade = DateTime.UtcNow.AddDays(DashboardDiasAlertaVencimento);
            var alertasVencimento = await dbContext.ItensEstoque
                .AsNoTracking()
                .CountAsync(i => i.EmpresaId == empresaId && i.ValidadeEm != null && i.ValidadeEm.DataValidade <= cutoffValidade);

            // Alertas parados
            var cutoffParado = DateTime.UtcNow.AddDays(-DashboardDiasAlertaParado);
            var alertasParados = await dbContext.ItensEstoque
                .AsNoTracking()
                .CountAsync(i => i.EmpresaId == empresaId &&
                    (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado));

            // Vendas do período
            var movData = await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de &&
                    m.DataMovimentacao <= ate)
                .Select(m => new { m.Quantidade.Value, ValorTotal = m.ValorTotal != null ? m.ValorTotal.Valor : 0m })
                .ToListAsync();

            var totalSaidasQtd = movData.Sum(m => m.Value);
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
                AlertasEstoqueBaixo: alertasEstoqueBaixo,
                AlertasVencimento: alertasVencimento,
                AlertasItensParados: alertasParados);

            await SetCachedAsync(cacheKey, result, DashboardTtl);
            return result;
        }

        // Receita

        public async Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12)
        {
            var cacheKey = $"analytics:receita:{empresaId}:{meses}";
            var cached = await GetCachedAsync<List<ReceitaPorPeriodo>>(cacheKey);
            if (cached is not null) return cached;

            var de = DateTime.UtcNow.AddMonths(-meses);

            var raw = await dbContext.Vendas
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.DataVenda >= de)
                .Select(v => new
                {
                    v.DataVenda.Year,
                    v.DataVenda.Month,
                    ValorTotal = v.ValorTotal.Valor,
                    v.ItensVenda
                })
                .ToListAsync();

            var result = raw
                .GroupBy(v => new { v.Year, v.Month })
                .Select(g =>
                {
                    var totalVendas = g.Count();
                    var receita = g.Sum(v => v.ValorTotal);
                    var totalItens = g.Sum(v => v.ItensVenda?.Sum(i => i.Quantidade.Value) ?? 0);
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

        // Margem

        public async Task<IReadOnlyList<MargemPorProduto>> GetMargemPorProdutoAsync(Guid empresaId, int dias = 30, int page = 1, int pageSize = 20)
        {
            var cacheKey = $"analytics:margem:{empresaId}:{dias}:{page}:{pageSize}";
            var cached = await GetCachedAsync<List<MargemPorProduto>>(cacheKey);
            if (cached is not null) return cached;

            var (de, _) = BuildPeriodo(dias);

            var raw = await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de &&
                    m.ValorUnitario != null)
                .Join(dbContext.Produtos.AsNoTracking(),
                    m => m.ProdutoId,
                    p => p.Id,
                    (m, p) => new
                    {
                        m.ProdutoId,
                        NomeProduto = p.Nome,
                        CustoUnitario = m.ItemEstoque != null ? m.ItemEstoque.CustoUnitario.Valor : (p.CustoReferencia != null ? p.CustoReferencia.Valor : 0m),
                        PrecoVenda = m.ValorUnitario!.Valor,
                        Quantidade = m.Quantidade.Value
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

        // Movimentacoes

        public async Task<IReadOnlyList<MovimentacaoResumo>> GetMovimentacoesResumoAsync(
            Guid empresaId,
            DateTime de,
            DateTime ate,
            TipoMovimentacaoEstoque? tipo = null)
        {
            var cacheKey = $"analytics:movimentacoes:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{tipo}";
            var cached = await GetCachedAsync<List<MovimentacaoResumo>>(cacheKey);
            if (cached is not null) return cached;

            var query = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.DataMovimentacao >= de &&
                    m.DataMovimentacao <= ate);

            if (tipo.HasValue)
                query = query.Where(m => m.Tipo == tipo.Value);

            var raw = await query
                .Select(m => new
                {
                    m.DataMovimentacao.Year,
                    m.DataMovimentacao.Month,
                    m.DataMovimentacao.Day,
                    m.Tipo,
                    Quantidade = m.Quantidade.Value,
                    Valor = m.ValorTotal != null ? m.ValorTotal.Valor : 0m
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

        // Validade

        public async Task<(IReadOnlyList<ValidadeAlerta> Items, int TotalCount)> GetAlertasValidadeAsync(
            Guid empresaId, int dias = 30, int page = 1, int pageSize = 20)
        {
            var cacheKey = $"analytics:validade:{empresaId}:{dias}:{page}:{pageSize}";
            var cached = await GetCachedAsync<(List<ValidadeAlerta>, int)>(cacheKey);
            if (cached != default) return cached;

            var cutoff = DateTime.UtcNow.AddDays(dias);
            var hoje = DateTime.UtcNow.Date;

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    i.ValidadeEm != null &&
                    i.ValidadeEm.DataValidade <= cutoff &&
                    i.QuantidadeAtual.Value > 0);

            var totalCount = await query.CountAsync();

            var raw = await query
                .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
                .OrderBy(x => x.i.ValidadeEm!.DataValidade)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.i.Id,
                    x.i.ProdutoId,
                    NomeProduto = x.p.Nome,
                    x.i.CodigoInterno,
                    Quantidade = x.i.QuantidadeAtual.Value,
                    DataValidade = x.i.ValidadeEm!.DataValidade,
                    Custo = x.i.CustoUnitario.Valor
                })
                .ToListAsync();

            var items = raw.Select(x => new ValidadeAlerta(
                ItemEstoqueId: x.Id,
                ProdutoId: x.ProdutoId,
                NomeProduto: x.NomeProduto,
                CodigoInterno: x.CodigoInterno,
                QuantidadeAtual: x.Quantidade,
                DataValidade: x.DataValidade,
                DiasAteVencimento: Math.Max(0, (x.DataValidade.Date - hoje).Days),
                ValorEmRisco: Math.Round(x.Quantidade * x.Custo, 2)))
                .ToList();

            await SetCachedAsync(cacheKey, (items, totalCount), ValidadeTtl);
            return (items, totalCount);
        }

        // Parados

        public async Task<(IReadOnlyList<ItemParadoDetalhe> Items, int TotalCount)> GetItensParadosDetalhadosAsync(
            Guid empresaId, int diasSemMovimento = 90, int page = 1, int pageSize = 20)
        {
            var cacheKey = $"analytics:parados:{empresaId}:{diasSemMovimento}:{page}:{pageSize}";
            var cached = await GetCachedAsync<(List<ItemParadoDetalhe>, int)>(cacheKey);
            if (cached != default) return cached;

            var cutoff = DateTime.UtcNow.AddDays(-diasSemMovimento);
            var hoje = DateTime.UtcNow;

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    i.QuantidadeAtual.Value > 0 &&
                    (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoff));

            var totalCount = await query.CountAsync();

            var raw = await query
                .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
                .OrderBy(x => x.i.UltimaMovimentacaoEm ?? DateTime.MinValue)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.i.Id,
                    x.i.ProdutoId,
                    NomeProduto = x.p.Nome,
                    x.i.CodigoInterno,
                    Quantidade = x.i.QuantidadeAtual.Value,
                    x.i.UltimaMovimentacaoEm,
                    Custo = x.i.CustoUnitario.Valor
                })
                .ToListAsync();

            var items = raw.Select(x => new ItemParadoDetalhe(
                ItemEstoqueId: x.Id,
                ProdutoId: x.ProdutoId,
                NomeProduto: x.NomeProduto,
                CodigoInterno: x.CodigoInterno,
                QuantidadeAtual: x.Quantidade,
                UltimaMovimentacaoEm: x.UltimaMovimentacaoEm,
                DiasSemMovimentacao: (int)(hoje - (x.UltimaMovimentacaoEm ?? hoje.AddDays(-diasSemMovimento))).TotalDays,
                ValorParado: Math.Round(x.Quantidade * x.Custo, 2)))
                .ToList();

            await SetCachedAsync(cacheKey, (items, totalCount), ParadosTtl);
            return (items, totalCount);
        }

        // Sazonalidade

        public async Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12)
        {
            var cacheKey = $"analytics:sazonalidade:{empresaId}:{produtoId}:{meses}";
            var cached = await GetCachedAsync<List<SazonalidadeMensal>>(cacheKey);
            if (cached is not null) return cached;

            var de = DateTime.UtcNow.AddMonths(-meses);

            var raw = await dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.ProdutoId == produtoId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de)
                .Select(m => new
                {
                    m.DataMovimentacao.Year,
                    m.DataMovimentacao.Month,
                    Quantidade = m.Quantidade.Value,
                    Valor = m.ValorTotal != null ? m.ValorTotal.Valor : 0m
                })
                .ToListAsync();

            var agregados = raw
                .GroupBy(m => new { m.Year, m.Month })
                .Select(g => new
                {
                    Ano = g.Key.Year,
                    Mes = g.Key.Month,
                    TotalSaidas = g.Sum(m => m.Quantidade),
                    ValorTotal = g.Sum(m => m.Valor)
                })
                .OrderBy(x => x.Ano).ThenBy(x => x.Mes)
                .ToList();

            // Média móvel 3 meses
            var result = agregados.Select((item, idx) =>
            {
                var janela = agregados.Skip(Math.Max(0, idx - 2)).Take(Math.Min(3, idx + 1));
                var media = janela.Any() ? janela.Average(x => (double)x.TotalSaidas) : 0d;
                return new SazonalidadeMensal(
                    Ano: item.Ano,
                    Mes: item.Mes,
                    TotalSaidas: item.TotalSaidas,
                    ValorTotal: Math.Round(item.ValorTotal, 2),
                    MediaMovelTresMeses: Math.Round((decimal)media, 2));
            }).ToList();

            await SetCachedAsync(cacheKey, result, SazonalidadeTtl);
            return result;
        }

        // Reposicao sugerida

        public async Task<(IReadOnlyList<ReposicaoSugerida> Items, int TotalCount)> GetSugestaoReposicaoDetalhadaAsync(
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20)
        {
            var cacheKey = $"analytics:reposicao:{empresaId}:{diasHistorico}:{page}:{pageSize}";
            var cached = await GetCachedAsync<(List<ReposicaoSugerida>, int)>(cacheKey);
            if (cached != default) return cached;

            var (de, ate) = BuildPeriodo(diasHistorico);
            var dias = Math.Max(1, diasHistorico);

            var taxas = await GetTaxasSaidaAsync(empresaId, de, ate, dias);

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.QuantidadeAtual.Value < i.QuantidadeMinima);

            var totalCount = await query.CountAsync();

            var raw = await query
                .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
                .OrderBy(x => x.i.QuantidadeAtual.Value)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.i.Id,
                    x.i.ProdutoId,
                    NomeProduto = x.p.Nome,
                    x.i.CodigoInterno,
                    Quantidade = x.i.QuantidadeAtual.Value,
                    x.i.QuantidadeMinima,
                    Custo = x.i.CustoUnitario.Valor
                })
                .ToListAsync();

            var items = raw.Select(x =>
            {
                var taxa = taxas.TryGetValue(x.ProdutoId, out var t) ? t : 0m;
                var diasAte = taxa > 0 ? (int?)Math.Floor(x.Quantidade / taxa) : null;
                // Sugestão: repor para 30 dias de cobertura acima do mínimo
                var coberturaSugerida = taxa > 0 ? (int)Math.Ceiling(taxa * 30) : x.QuantidadeMinima * 2;
                var qtdRepor = Math.Max(0, coberturaSugerida - x.Quantidade);
                return new ReposicaoSugerida(
                    ItemEstoqueId: x.Id,
                    ProdutoId: x.ProdutoId,
                    NomeProduto: x.NomeProduto,
                    CodigoInterno: x.CodigoInterno,
                    QuantidadeAtual: x.Quantidade,
                    QuantidadeMinima: x.QuantidadeMinima,
                    QuantidadeSugeridaReposicao: qtdRepor,
                    VelocidadeSaidaDiaria: Math.Round(taxa, 2),
                    DiasAteRuptura: diasAte,
                    CustoEstimadoReposicao: Math.Round(qtdRepor * x.Custo, 2));
            }).ToList();

            await SetCachedAsync(cacheKey, (items, totalCount), ReposicaoTtl);
            return (items, totalCount);
        }

        // Projecao de ruptura

        public async Task<(IReadOnlyList<ProjecaoRuptura> Items, int TotalCount)> GetProjecaoRupturaAsync(
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20)
        {
            var cacheKey = $"analytics:projecao:{empresaId}:{diasHistorico}:{page}:{pageSize}";
            var cached = await GetCachedAsync<(List<ProjecaoRuptura>, int)>(cacheKey);
            if (cached != default) return cached;

            var (de, ate) = BuildPeriodo(diasHistorico);
            var dias = Math.Max(1, diasHistorico);

            var taxas = await GetTaxasSaidaAsync(empresaId, de, ate, dias);

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.QuantidadeAtual.Value > 0);

            var totalCount = await query.CountAsync();

            var raw = await query
                .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
                .OrderBy(x => x.i.ProdutoId)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.i.Id,
                    x.i.ProdutoId,
                    NomeProduto = x.p.Nome,
                    x.i.CodigoInterno,
                    Quantidade = x.i.QuantidadeAtual.Value
                })
                .ToListAsync();

            var agora = DateTime.UtcNow;

            var items = raw.Select(x =>
            {
                var taxa = taxas.TryGetValue(x.ProdutoId, out var t) ? t : 0m;
                var diasAte = taxa > 0 ? (int?)Math.Floor(x.Quantidade / taxa) : null;
                return new ProjecaoRuptura(
                    ItemEstoqueId: x.Id,
                    ProdutoId: x.ProdutoId,
                    NomeProduto: x.NomeProduto,
                    CodigoInterno: x.CodigoInterno,
                    QuantidadeAtual: x.Quantidade,
                    TaxaSaidaDiaria: Math.Round(taxa, 2),
                    DiasAteRuptura: diasAte,
                    DataEstimadaRuptura: diasAte.HasValue ? agora.AddDays(diasAte.Value) : null);
            })
            .OrderBy(x => x.DiasAteRuptura ?? int.MaxValue)
            .ToList();

            await SetCachedAsync(cacheKey, (items, totalCount), ProjecaoTtl);
            return (items, totalCount);
        }

        // Vendas por canal

        public async Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate)
        {
            var cacheKey = $"analytics:canal:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
            var cached = await GetCachedAsync<List<VendaPorCanal>>(cacheKey);
            if (cached is not null) return cached;

            var raw = await dbContext.Vendas
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.DataVenda >= de && v.DataVenda <= ate)
                .Select(v => new
                {
                    v.Canal,
                    ValorTotal = v.ValorTotal.Valor,
                    v.ItensVenda
                })
                .ToListAsync();

            var totalReceita = raw.Sum(v => v.ValorTotal);

            var result = raw
                .GroupBy(v => v.Canal)
                .Select(g =>
                {
                    var totalVendas = g.Count();
                    var receita = g.Sum(v => v.ValorTotal);
                    var totalItens = g.Sum(v => v.ItensVenda?.Sum(i => i.Quantidade.Value) ?? 0);
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
}
