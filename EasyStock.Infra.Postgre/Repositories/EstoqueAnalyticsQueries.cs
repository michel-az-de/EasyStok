using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>Consultas de analytics para estoque: validade, parados, sazonalidade, reposição e projeção de ruptura.</summary>
internal sealed class EstoqueAnalyticsQueries(EasyStockDbContext dbContext, IDistributedCache? cache = null)
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

    private static readonly TimeSpan ValidadeTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ParadosTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SazonalidadeTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ReposicaoTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ProjecaoTtl = TimeSpan.FromMinutes(5);

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
                (DateTime?)i.ValidadeEm <= cutoff &&
                (int)i.QuantidadeAtual > 0);
        if (lojaId.HasValue)
            query = query.Where(i => i.LojaId == lojaId.Value);

        var totalCount = await query.CountAsync();

        var raw = await query
            .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
            .OrderBy(x => (DateTime?)x.i.ValidadeEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.i.Id,
                x.i.ProdutoId,
                NomeProduto = x.p.Nome,
                x.i.CodigoInterno,
                Quantidade = (int)x.i.QuantidadeAtual,
                DataValidade = (DateTime)x.i.ValidadeEm!,
                Custo = (decimal)x.i.CustoUnitario
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
                (int)i.QuantidadeAtual > 0 &&
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
                Quantidade = (int)x.i.QuantidadeAtual,
                x.i.UltimaMovimentacaoEm,
                Custo = (decimal)x.i.CustoUnitario
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
                Quantidade = (int)m.Quantidade,
                Valor = (decimal?)m.ValorTotal ?? 0m
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
            .Select(g => new { ProdutoId = g.Key, Total = g.Sum(m => (int)m.Quantidade) })
            .ToDictionaryAsync(x => x.ProdutoId, x => (decimal)x.Total / dias);

        var query = dbContext.ItensEstoque
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && (int)i.QuantidadeAtual < i.QuantidadeMinima);
        if (lojaId.HasValue)
            query = query.Where(i => i.LojaId == lojaId.Value);

        var totalCount = await query.CountAsync();

        var raw = await query
            .Join(dbContext.Produtos.AsNoTracking(), i => i.ProdutoId, p => p.Id, (i, p) => new { i, p })
            .OrderBy(x => (int)x.i.QuantidadeAtual)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.i.Id,
                x.i.ProdutoId,
                NomeProduto = x.p.Nome,
                x.i.CodigoInterno,
                Quantidade = (int)x.i.QuantidadeAtual,
                x.i.QuantidadeMinima,
                Custo = (decimal)x.i.CustoUnitario
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
            .Select(g => new { ProdutoId = g.Key, Total = g.Sum(m => (int)m.Quantidade) })
            .ToDictionaryAsync(x => x.ProdutoId, x => (decimal)x.Total / dias);

        var query = dbContext.ItensEstoque
            .AsNoTracking()
            .Where(i => i.EmpresaId == empresaId && (int)i.QuantidadeAtual > 0);
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
                Quantidade = (int)x.i.QuantidadeAtual
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
}
