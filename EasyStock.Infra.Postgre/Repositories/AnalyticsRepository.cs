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
        private static readonly TimeSpan ReceitaTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MargemTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan MovimentacaoTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ValidadeTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ParadosTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan SazonalidadeTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ReposicaoTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan ProjecaoTtl = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CanalTtl = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan ComparacaoTtl = TimeSpan.FromMinutes(5);

        // Dashboard

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
                .Where(i => i.EmpresaId == empresaId);
            if (lojaId.HasValue)
                estoqueQuery = estoqueQuery.Where(i => i.LojaId == lojaId.Value);

            var estoqueData = await estoqueQuery.Select(i => new
            {
                Quantidade = EF.Property<int>(i, "QuantidadeAtual"),
                ValorCusto = EF.Property<decimal>(i, "CustoUnitario") * EF.Property<int>(i, "QuantidadeAtual"),
                ValorVenda = (EF.Property<decimal?>(i, "PrecoVendaSugerido")
                    ?? EF.Property<decimal>(i, "CustoUnitario") * 1.3m) * EF.Property<int>(i, "QuantidadeAtual"),
                EstaAbaixoMinimo = EF.Property<int>(i, "QuantidadeAtual") < i.QuantidadeMinima
            }).ToListAsync();

            var totalSkus = await estoqueQuery.Select(i => i.ProdutoId).Distinct().CountAsync();
            var totalQtd = estoqueData.Sum(e => e.Quantidade);
            var valorCusto = estoqueData.Sum(e => e.ValorCusto);
            var valorVenda = estoqueData.Sum(e => e.ValorVenda);
            var alertasEstoqueBaixo = estoqueData.Count(e => e.EstaAbaixoMinimo);

            // Alertas vencimento (30 dias)
            var cutoffValidade = DateTime.UtcNow.AddDays(30);
            var validadeQuery = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && i.ValidadeEm != null && EF.Property<DateTime?>(i, "ValidadeEm") <= cutoffValidade);
            if (lojaId.HasValue)
                validadeQuery = validadeQuery.Where(i => i.LojaId == lojaId.Value);
            var alertasVencimento = await validadeQuery.CountAsync();

            // Alertas parados (90 dias)
            var cutoffParado = DateTime.UtcNow.AddDays(-90);
            var paradosQuery = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado));
            if (lojaId.HasValue)
                paradosQuery = paradosQuery.Where(i => i.LojaId == lojaId.Value);
            var alertasParados = await paradosQuery.CountAsync();

            // Vendas do período
            var movQuery = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de &&
                    m.DataMovimentacao <= ate);
            if (lojaId.HasValue)
                movQuery = movQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

            var movData = await movQuery
                .Select(m => new { Quantidade = EF.Property<int>(m, "Quantidade"), ValorTotal = EF.Property<decimal?>(m, "ValorTotal") ?? 0m })
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
                AlertasEstoqueBaixo: alertasEstoqueBaixo,
                AlertasVencimento: alertasVencimento,
                AlertasItensParados: alertasParados);

            await SetCachedAsync(cacheKey, result, DashboardTtl);
            return result;
        }

        // Receita

        public async Task<IReadOnlyList<ReceitaPorPeriodo>> GetReceitaPorPeriodoAsync(Guid empresaId, int meses = 12, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:receita:{empresaId}:{meses}:{lojaId}";
            var cached = await GetCachedAsync<List<ReceitaPorPeriodo>>(cacheKey);
            if (cached is not null) return cached;

            var de = DateTime.UtcNow.AddMonths(-meses);

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
                    ValorTotal = EF.Property<decimal>(v, "ValorTotal"),
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
                    (m, p) => new
                    {
                        m.ProdutoId,
                        NomeProduto = p.Nome,
                        CustoUnitario = m.ItemEstoque != null ? EF.Property<decimal>(m.ItemEstoque, "CustoUnitario") : (p.CustoReferencia != null ? EF.Property<decimal>(p, "CustoReferencia") : 0m),
                        PrecoVenda = EF.Property<decimal>(m, "ValorUnitario"),
                        Quantidade = EF.Property<int>(m, "Quantidade")
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
                    Quantidade = EF.Property<int>(m, "Quantidade"),
                    Valor = EF.Property<decimal?>(m, "ValorTotal") ?? 0m
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
            Guid empresaId, int dias = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:validade:{empresaId}:{dias}:{page}:{pageSize}:{lojaId}";
            var cached = await GetCachedAsync<(List<ValidadeAlerta>, int)>(cacheKey);
            if (cached != default) return cached;

            var cutoff = DateTime.UtcNow.AddDays(dias);
            var hoje = DateTime.UtcNow.Date;

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    i.ValidadeEm != null &&
                    EF.Property<DateTime?>(i, "ValidadeEm") <= cutoff &&
                    EF.Property<int>(i, "QuantidadeAtual") > 0);
            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

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
                    Quantidade = EF.Property<int>(x.i, "QuantidadeAtual"),
                    DataValidade = x.i.ValidadeEm!.DataValidade,
                    Custo = EF.Property<decimal>(x.i, "CustoUnitario")
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
            Guid empresaId, int diasSemMovimento = 90, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:parados:{empresaId}:{diasSemMovimento}:{page}:{pageSize}:{lojaId}";
            var cached = await GetCachedAsync<(List<ItemParadoDetalhe>, int)>(cacheKey);
            if (cached != default) return cached;

            var cutoff = DateTime.UtcNow.AddDays(-diasSemMovimento);
            var hoje = DateTime.UtcNow;

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId &&
                    EF.Property<int>(i, "QuantidadeAtual") > 0 &&
                    (i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoff));
            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

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
                    Quantidade = EF.Property<int>(x.i, "QuantidadeAtual"),
                    x.i.UltimaMovimentacaoEm,
                    Custo = EF.Property<decimal>(x.i, "CustoUnitario")
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

        public async Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:sazonalidade:{empresaId}:{produtoId}:{meses}:{lojaId}";
            var cached = await GetCachedAsync<List<SazonalidadeMensal>>(cacheKey);
            if (cached is not null) return cached;

            var de = DateTime.UtcNow.AddMonths(-meses);

            var movQuery = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.ProdutoId == produtoId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de);
            if (lojaId.HasValue)
                movQuery = movQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

            var raw = await movQuery
                .Select(m => new
                {
                    m.DataMovimentacao.Year,
                    m.DataMovimentacao.Month,
                    Quantidade = EF.Property<int>(m, "Quantidade"),
                    Valor = EF.Property<decimal?>(m, "ValorTotal") ?? 0m
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
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:reposicao:{empresaId}:{diasHistorico}:{page}:{pageSize}:{lojaId}";
            var cached = await GetCachedAsync<(List<ReposicaoSugerida>, int)>(cacheKey);
            if (cached != default) return cached;

            var de = DateTime.UtcNow.AddDays(-diasHistorico);
            var ate = DateTime.UtcNow;
            var dias = Math.Max(1, diasHistorico);

            // Taxa de saída por produto
            var taxaQuery = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de &&
                    m.DataMovimentacao <= ate);
            if (lojaId.HasValue)
                taxaQuery = taxaQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

            var taxas = await taxaQuery
                .GroupBy(m => m.ProdutoId)
                .Select(g => new { ProdutoId = g.Key, Total = g.Sum(m => EF.Property<int>(m, "Quantidade")) })
                .ToDictionaryAsync(x => x.ProdutoId, x => (decimal)x.Total / dias);

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && EF.Property<int>(i, "QuantidadeAtual") < i.QuantidadeMinima);
            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

            var totalCount = await query.CountAsync();

            var raw = await query
                .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
                .OrderBy(x => EF.Property<int>(x.i, "QuantidadeAtual"))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => new
                {
                    x.i.Id,
                    x.i.ProdutoId,
                    NomeProduto = x.p.Nome,
                    x.i.CodigoInterno,
                    Quantidade = EF.Property<int>(x.i, "QuantidadeAtual"),
                    x.i.QuantidadeMinima,
                    Custo = EF.Property<decimal>(x.i, "CustoUnitario")
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
            Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:projecao:{empresaId}:{diasHistorico}:{page}:{pageSize}:{lojaId}";
            var cached = await GetCachedAsync<(List<ProjecaoRuptura>, int)>(cacheKey);
            if (cached != default) return cached;

            var de = DateTime.UtcNow.AddDays(-diasHistorico);
            var ate = DateTime.UtcNow;
            var dias = Math.Max(1, diasHistorico);

            var taxaQuery = dbContext.MovimentacoesEstoque
                .AsNoTracking()
                .Where(m => m.EmpresaId == empresaId &&
                    m.Tipo == TipoMovimentacaoEstoque.Saida &&
                    m.DataMovimentacao >= de &&
                    m.DataMovimentacao <= ate);
            if (lojaId.HasValue)
                taxaQuery = taxaQuery.Where(m => m.ItemEstoque != null && m.ItemEstoque.LojaId == lojaId.Value);

            var taxas = await taxaQuery
                .GroupBy(m => m.ProdutoId)
                .Select(g => new { ProdutoId = g.Key, Total = g.Sum(m => EF.Property<int>(m, "Quantidade")) })
                .ToDictionaryAsync(x => x.ProdutoId, x => (decimal)x.Total / dias);

            var query = dbContext.ItensEstoque
                .AsNoTracking()
                .Where(i => i.EmpresaId == empresaId && EF.Property<int>(i, "QuantidadeAtual") > 0);
            if (lojaId.HasValue)
                query = query.Where(i => i.LojaId == lojaId.Value);

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
                    Quantidade = EF.Property<int>(x.i, "QuantidadeAtual")
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

        public async Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate, Guid? lojaId = null)
        {
            var cacheKey = $"analytics:canal:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}:{lojaId}";
            var cached = await GetCachedAsync<List<VendaPorCanal>>(cacheKey);
            if (cached is not null) return cached;

            var vendasQuery = dbContext.Vendas
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId && v.DataVenda >= de && v.DataVenda <= ate);
            if (lojaId.HasValue)
                vendasQuery = vendasQuery.Where(v => v.LojaId == lojaId.Value);

            var raw = await vendasQuery
                .Select(v => new
                {
                    v.Canal,
                    ValorTotal = EF.Property<decimal>(v, "ValorTotal"),
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

        // ── Store Intelligence Methods ──────────────────────────────────────

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
                    QuantidadeTotal = g.Sum(i => EF.Property<int>(i, "QuantidadeAtual")),
                    ValorEstoque = g.Sum(i => (i.PrecoVendaSugerido != null ? EF.Property<decimal?>(i, "PrecoVendaSugerido") : EF.Property<decimal>(i, "CustoUnitario") * 1.3m) * EF.Property<int>(i, "QuantidadeAtual")),
                    AlertasCriticos = g.Count(i => EF.Property<int>(i, "QuantidadeAtual") <= 2),
                    ItensAbaixoMinimo = g.Count(i => EF.Property<int>(i, "QuantidadeAtual") < i.QuantidadeMinima),
                    AlertasVencimento = g.Count(i => i.ValidadeEm != null && EF.Property<DateTime?>(i, "ValidadeEm") <= cutoffValidade),
                    ItensParados = g.Count(i => i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado),
                    TotalItens = g.Count(),
                    ValorVencendo = g.Where(i => i.ValidadeEm != null && EF.Property<DateTime?>(i, "ValidadeEm") <= cutoffValidade)
                        .Sum(i => EF.Property<decimal>(i, "CustoUnitario") * EF.Property<int>(i, "QuantidadeAtual"))
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
                    TotalSaidas = g.Sum(m => EF.Property<int>(m, "Quantidade")),
                    ReceitaTotal = g.Sum(m => m.ValorTotal != null ? EF.Property<decimal?>(m, "ValorTotal") : 0m)
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

            var dashboard = await GetDashboardResumoAsync(empresaId, periodoDias, lojaId);

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
                    IsCritical = EF.Property<int>(i, "QuantidadeAtual") <= 2,
                    IsBelowMin = EF.Property<int>(i, "QuantidadeAtual") < i.QuantidadeMinima,
                    IsExpiring = i.ValidadeEm != null && EF.Property<DateTime?>(i, "ValidadeEm") <= cutoffValidade,
                    IsIdle = i.UltimaMovimentacaoEm == null || i.UltimaMovimentacaoEm < cutoffParado,
                    ValorVencendo = i.ValidadeEm != null && EF.Property<DateTime?>(i, "ValidadeEm") <= cutoffValidade
                        ? EF.Property<decimal>(i, "CustoUnitario") * EF.Property<int>(i, "QuantidadeAtual") : 0m
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
                .SumAsync(m => EF.Property<int>(m, "Quantidade"));
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
                    Quantidade = EF.Property<int>(m, "Quantidade"),
                    Valor = EF.Property<decimal?>(m, "ValorTotal") ?? 0m
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

        // ── Health Score Calculation ─────────────────────────────────────

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
