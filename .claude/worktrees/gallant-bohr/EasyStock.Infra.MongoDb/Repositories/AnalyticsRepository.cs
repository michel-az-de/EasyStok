using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.MongoDb.Data;
using Microsoft.Extensions.Caching.Distributed;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EasyStock.Infra.MongoDb.Repositories;

/// <summary>
/// Implements aggregated analytics queries against MongoDB with optional Redis distributed cache (5–10 min TTL).
/// </summary>
public sealed class AnalyticsRepository(MongoEasyStockContext context, IDistributedCache? cache = null)
    : IAnalyticsRepository
{
    private IMongoCollection<ItemEstoque> ItensEstoque => context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque);
    private IMongoCollection<Produto> Produtos => context.GetCollection<Produto>(MongoCollectionNames.Produtos);
    private IMongoCollection<MovimentacaoEstoque> MovimentacoesEstoque => context.GetCollection<MovimentacaoEstoque>(MongoCollectionNames.MovimentacoesEstoque);
    private IMongoCollection<Venda> Vendas => context.GetCollection<Venda>(MongoCollectionNames.Vendas);

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

    // Dashboard

    public async Task<DashboardResumo> GetDashboardResumoAsync(Guid empresaId, int periodoDias = 30)
    {
        var cacheKey = $"analytics:dashboard:{empresaId}:{periodoDias}";
        var cached = await GetCachedAsync<DashboardResumo>(cacheKey);
        if (cached is not null) return cached;

        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-periodoDias);

        // Estoque
        var estoquePipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("EmpresaId", empresaId)),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "QuantidadeEmEstoque", new BsonDocument("$sum", "$QuantidadeAtual.Value") },
                { "ValorCusto", new BsonDocument("$sum", new BsonDocument("$multiply", new BsonArray { "$QuantidadeAtual.Value", "$CustoUnitario.Valor" })) },
                { "ValorVenda", new BsonDocument("$sum", new BsonDocument("$multiply", new BsonArray {
                    "$QuantidadeAtual.Value",
                    new BsonDocument("$ifNull", new BsonArray {
                        "$PrecoVendaSugerido.Valor",
                        new BsonDocument("$multiply", new BsonArray { "$CustoUnitario.Valor", 1.3m })
                    })
                })) },
                { "AlertasEstoqueBaixo", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray {
                    new BsonDocument("$lt", new BsonArray { "$QuantidadeAtual.Value", "$QuantidadeMinima" }),
                    1, 0
                })) }
            })
        };

        var estoqueData = await ItensEstoque.Aggregate<BsonDocument>(estoquePipeline).FirstOrDefaultAsync();
        var totalQtd = estoqueData?["QuantidadeEmEstoque"].ToInt32() ?? 0;
        var valorCusto = estoqueData?["ValorCusto"].ToDecimal() ?? 0m;
        var valorVenda = estoqueData?["ValorVenda"].ToDecimal() ?? 0m;
        var alertasEstoqueBaixo = estoqueData?["AlertasEstoqueBaixo"].ToInt32() ?? 0;

        var totalSkus = await ItensEstoque.Distinct<Guid>("ProdutoId", new BsonDocument("EmpresaId", empresaId)).ToListAsync().ContinueWith(t => t.Result.Count);

        // Alertas vencimento
        var cutoffValidade = DateTime.UtcNow.AddDays(30);
        var alertasVencimento = await ItensEstoque.CountDocumentsAsync(new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "ValidadeEm.DataValidade", new BsonDocument("$lte", cutoffValidade) },
            { "QuantidadeAtual.Value", new BsonDocument("$gt", 0) }
        });

        // Alertas parados
        var cutoffParado = DateTime.UtcNow.AddDays(-90);
        var alertasParados = await ItensEstoque.CountDocumentsAsync(new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "QuantidadeAtual.Value", new BsonDocument("$gt", 0) },
            { "$or", new BsonArray {
                new BsonDocument("UltimaMovimentacaoEm", BsonNull.Value),
                new BsonDocument("UltimaMovimentacaoEm", new BsonDocument("$lt", cutoffParado))
            }}
        });

        // Vendas do periodo
        var movPipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "Tipo", TipoMovimentacaoEstoque.Saida },
                { "DataMovimentacao", new BsonDocument("$gte", de).Add("$lte", ate) }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "TotalSaidasQtd", new BsonDocument("$sum", "$Quantidade.Value") },
                { "ReceitaEstimada", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$ValorTotal.Valor", 0m })) }
            })
        };

        var movData = await MovimentacoesEstoque.Aggregate<BsonDocument>(movPipeline).FirstOrDefaultAsync();
        var totalSaidasQtd = movData?["TotalSaidasQtd"].ToInt32() ?? 0;
        var receitaEstimada = movData?["ReceitaEstimada"].ToDecimal() ?? 0m;
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
            AlertasVencimento: (int)alertasVencimento,
            AlertasItensParados: (int)alertasParados);

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

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "DataVenda", new BsonDocument("$gte", de) }
            }),
            new BsonDocument("$unwind", "$ItensVenda"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                {
                    { "Ano", new BsonDocument("$year", "$DataVenda") },
                    { "Mes", new BsonDocument("$month", "$DataVenda") }
                }},
                { "TotalVendas", new BsonDocument("$sum", 1) },
                { "Receita", new BsonDocument("$sum", "$ValorTotal.Valor") },
                { "TotalItens", new BsonDocument("$sum", "$ItensVenda.Quantidade.Value") }
            }),
            new BsonDocument("$sort", new BsonDocument("_id.Ano", 1).Add("_id.Mes", 1))
        };

        var raw = await Vendas.Aggregate<BsonDocument>(pipeline).ToListAsync();

        var result = raw.Select(x => new ReceitaPorPeriodo(
            Ano: x["_id"]["Ano"].ToInt32(),
            Mes: x["_id"]["Mes"].ToInt32(),
            ReceitaBruta: Math.Round(x["Receita"].ToDecimal(), 2),
            TotalVendas: x["TotalVendas"].ToInt32(),
            TotalItensVendidos: x["TotalItens"].ToInt32(),
            TicketMedio: x["TotalVendas"].ToInt32() > 0 ? Math.Round(x["Receita"].ToDecimal() / x["TotalVendas"].ToInt32(), 2) : 0m))
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

        var de = DateTime.UtcNow.AddDays(-dias);

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "Tipo", TipoMovimentacaoEstoque.Saida },
                { "DataMovimentacao", new BsonDocument("$gte", de) },
                { "ValorUnitario", new BsonDocument("$ne", BsonNull.Value) }
            }),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", "Produtos" },
                { "localField", "ProdutoId" },
                { "foreignField", "_id" },
                { "as", "produto" }
            }),
            new BsonDocument("$unwind", "$produto"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$ProdutoId" },
                { "NomeProduto", new BsonDocument("$first", "$produto.Nome") },
                { "CustoMedio", new BsonDocument("$avg", new BsonDocument("$ifNull", new BsonArray {
                    "$ItemEstoque.CustoUnitario.Valor",
                    new BsonDocument("$ifNull", new BsonArray { "$produto.CustoReferencia.Valor", 0m })
                })) },
                { "PrecoMedio", new BsonDocument("$avg", "$ValorUnitario.Valor") },
                { "QuantidadeVendida", new BsonDocument("$sum", "$Quantidade.Value") }
            }),
            new BsonDocument("$project", new BsonDocument
            {
                { "ProdutoId", "$_id" },
                { "NomeProduto", 1 },
                { "CustoMedio", 1 },
                { "PrecoMedio", 1 },
                { "QuantidadeVendida", 1 },
                { "MargemAbs", new BsonDocument("$subtract", new BsonArray { "$PrecoMedio", "$CustoMedio" }) },
                { "MargemPct", new BsonDocument("$cond", new BsonArray {
                    new BsonDocument("$gt", new BsonArray { "$CustoMedio", 0 }),
                    new BsonDocument("$multiply", new BsonArray {
                        new BsonDocument("$divide", new BsonArray { new BsonDocument("$subtract", new BsonArray { "$PrecoMedio", "$CustoMedio" }), "$CustoMedio" }),
                        100
                    }),
                    0
                }) }
            }),
            new BsonDocument("$sort", new BsonDocument("MargemPct", -1)),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize)
        };

        var raw = await MovimentacoesEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();

        var result = raw.Select(x => new MargemPorProduto(
            ProdutoId: x["ProdutoId"].AsGuid,
            NomeProduto: x["NomeProduto"].AsString,
            CustoMedio: Math.Round(x["CustoMedio"].ToDecimal(), 2),
            PrecoMedioVenda: Math.Round(x["PrecoMedio"].ToDecimal(), 2),
            MargemAbsoluta: Math.Round(x["MargemAbs"].ToDecimal(), 2),
            MargemPercentual: Math.Round(x["MargemPct"].ToDecimal(), 2),
            QuantidadeVendida: x["QuantidadeVendida"].ToInt32()))
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

        var match = new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "DataMovimentacao", new BsonDocument("$gte", de).Add("$lte", ate) }
        };
        if (tipo.HasValue)
            match.Add("Tipo", tipo.Value);

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                {
                    { "Ano", new BsonDocument("$year", "$DataMovimentacao") },
                    { "Mes", new BsonDocument("$month", "$DataMovimentacao") },
                    { "Dia", new BsonDocument("$dayOfMonth", "$DataMovimentacao") },
                    { "Tipo", "$Tipo" }
                }},
                { "TotalMovimentacoes", new BsonDocument("$sum", 1) },
                { "QuantidadeTotal", new BsonDocument("$sum", "$Quantidade.Value") },
                { "ValorTotal", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$ValorTotal.Valor", 0m })) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id.Ano", 1).Add("_id.Mes", 1).Add("_id.Dia", 1).Add("_id.Tipo", 1))
        };

        var raw = await MovimentacoesEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();

        var result = raw.Select(x => new MovimentacaoResumo(
            Ano: x["_id"]["Ano"].ToInt32(),
            Mes: x["_id"]["Mes"].ToInt32(),
            Dia: x["_id"]["Dia"].ToInt32(),
            Tipo: (TipoMovimentacaoEstoque)x["_id"]["Tipo"].AsInt32,
            TotalMovimentacoes: x["TotalMovimentacoes"].ToInt32(),
            QuantidadeTotal: x["QuantidadeTotal"].ToInt32(),
            ValorTotal: Math.Round(x["ValorTotal"].ToDecimal(), 2)))
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

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "ValidadeEm.DataValidade", new BsonDocument("$lte", cutoff) },
                { "QuantidadeAtual.Value", new BsonDocument("$gt", 0) }
            }),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", "Produtos" },
                { "localField", "ProdutoId" },
                { "foreignField", "_id" },
                { "as", "produto" }
            }),
            new BsonDocument("$unwind", "$produto"),
            new BsonDocument("$sort", new BsonDocument("ValidadeEm.DataValidade", 1)),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
            new BsonDocument("$project", new BsonDocument
            {
                { "Id", "$_id" },
                { "ProdutoId", 1 },
                { "NomeProduto", "$produto.Nome" },
                { "CodigoInterno", 1 },
                { "Quantidade", "$QuantidadeAtual.Value" },
                { "DataValidade", "$ValidadeEm.DataValidade" },
                { "Custo", "$CustoUnitario.Valor" }
            })
        };

        var raw = await ItensEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var totalCount = await ItensEstoque.CountDocumentsAsync(new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "ValidadeEm.DataValidade", new BsonDocument("$lte", cutoff) },
            { "QuantidadeAtual.Value", new BsonDocument("$gt", 0) }
        });

        var items = raw.Select(x => new ValidadeAlerta(
            ItemEstoqueId: x["Id"].AsGuid,
            ProdutoId: x["ProdutoId"].AsGuid,
            NomeProduto: x["NomeProduto"].AsString,
            CodigoInterno: x["CodigoInterno"].AsString,
            QuantidadeAtual: x["Quantidade"].ToInt32(),
            DataValidade: x["DataValidade"].ToUniversalTime(),
            DiasAteVencimento: Math.Max(0, (x["DataValidade"].ToUniversalTime().Date - hoje).Days),
            ValorEmRisco: Math.Round(x["Quantidade"].ToInt32() * x["Custo"].ToDecimal(), 2)))
            .ToList();

        await SetCachedAsync(cacheKey, (items, (int)totalCount), ValidadeTtl);
        return (items, (int)totalCount);
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

        var match = new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "QuantidadeAtual.Value", new BsonDocument("$gt", 0) },
            { "$or", new BsonArray {
                new BsonDocument("UltimaMovimentacaoEm", BsonNull.Value),
                new BsonDocument("UltimaMovimentacaoEm", new BsonDocument("$lt", cutoff))
            }}
        };

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", "Produtos" },
                { "localField", "ProdutoId" },
                { "foreignField", "_id" },
                { "as", "produto" }
            }),
            new BsonDocument("$unwind", "$produto"),
            new BsonDocument("$sort", new BsonDocument("UltimaMovimentacaoEm", 1)),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
            new BsonDocument("$project", new BsonDocument
            {
                { "Id", "$_id" },
                { "ProdutoId", 1 },
                { "NomeProduto", "$produto.Nome" },
                { "CodigoInterno", 1 },
                { "Quantidade", "$QuantidadeAtual.Value" },
                { "UltimaMovimentacaoEm", 1 },
                { "Custo", "$CustoUnitario.Valor" }
            })
        };

        var raw = await ItensEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var totalCount = await ItensEstoque.CountDocumentsAsync(match);

        var items = raw.Select(x => new ItemParadoDetalhe(
            ItemEstoqueId: x["Id"].AsGuid,
            ProdutoId: x["ProdutoId"].AsGuid,
            NomeProduto: x["NomeProduto"].AsString,
            CodigoInterno: x["CodigoInterno"].AsString,
            QuantidadeAtual: x["Quantidade"].ToInt32(),
            UltimaMovimentacaoEm: x["UltimaMovimentacaoEm"].IsBsonNull ? null : x["UltimaMovimentacaoEm"].ToUniversalTime(),
            DiasSemMovimentacao: (int)(hoje - (x["UltimaMovimentacaoEm"].IsBsonNull ? hoje.AddDays(-diasSemMovimento) : x["UltimaMovimentacaoEm"].ToUniversalTime())).TotalDays,
            ValorParado: Math.Round(x["Quantidade"].ToInt32() * x["Custo"].ToDecimal(), 2)))
            .ToList();

        await SetCachedAsync(cacheKey, (items, (int)totalCount), ParadosTtl);
        return (items, (int)totalCount);
    }

    // Sazonalidade

    public async Task<IReadOnlyList<SazonalidadeMensal>> GetSazonalidadeAsync(Guid empresaId, Guid produtoId, int meses = 12)
    {
        var cacheKey = $"analytics:sazonalidade:{empresaId}:{produtoId}:{meses}";
        var cached = await GetCachedAsync<List<SazonalidadeMensal>>(cacheKey);
        if (cached is not null) return cached;

        var de = DateTime.UtcNow.AddMonths(-meses);

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "ProdutoId", produtoId },
                { "Tipo", TipoMovimentacaoEstoque.Saida },
                { "DataMovimentacao", new BsonDocument("$gte", de) }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", new BsonDocument
                {
                    { "Ano", new BsonDocument("$year", "$DataMovimentacao") },
                    { "Mes", new BsonDocument("$month", "$DataMovimentacao") }
                }},
                { "TotalSaidas", new BsonDocument("$sum", "$Quantidade.Value") },
                { "ValorTotal", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$ValorTotal.Valor", 0m })) }
            }),
            new BsonDocument("$sort", new BsonDocument("_id.Ano", 1).Add("_id.Mes", 1))
        };

        var agregados = await MovimentacoesEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();

        // Media movel 3 meses
        var result = new List<SazonalidadeMensal>();
        for (int i = 0; i < agregados.Count; i++)
        {
            var item = agregados[i];
            var janela = agregados.Skip(Math.Max(0, i - 2)).Take(Math.Min(3, i + 1));
            var media = janela.Any() ? janela.Average(x => (double)x["TotalSaidas"].ToInt32()) : 0d;
            result.Add(new SazonalidadeMensal(
                Ano: item["_id"]["Ano"].ToInt32(),
                Mes: item["_id"]["Mes"].ToInt32(),
                TotalSaidas: item["TotalSaidas"].ToInt32(),
                ValorTotal: Math.Round(item["ValorTotal"].ToDecimal(), 2),
                MediaMovelTresMeses: Math.Round((decimal)media, 2)));
        }

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

        var de = DateTime.UtcNow.AddDays(-diasHistorico);
        var ate = DateTime.UtcNow;
        var dias = Math.Max(1, diasHistorico);

        // Taxa de saida por produto
        var taxasPipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "Tipo", TipoMovimentacaoEstoque.Saida },
                { "DataMovimentacao", new BsonDocument("$gte", de).Add("$lte", ate) }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$ProdutoId" },
                { "Total", new BsonDocument("$sum", "$Quantidade.Value") }
            })
        };

        var taxasRaw = await MovimentacoesEstoque.Aggregate<BsonDocument>(taxasPipeline).ToListAsync();
        var taxas = taxasRaw.ToDictionary(x => x["_id"].AsGuid, x => (decimal)x["Total"].ToInt32() / dias);

        var match = new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "$expr", new BsonDocument("$lt", new BsonArray { "$QuantidadeAtual.Value", "$QuantidadeMinima" }) }
        };

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", "Produtos" },
                { "localField", "ProdutoId" },
                { "foreignField", "_id" },
                { "as", "produto" }
            }),
            new BsonDocument("$unwind", "$produto"),
            new BsonDocument("$sort", new BsonDocument("QuantidadeAtual.Value", 1)),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
            new BsonDocument("$project", new BsonDocument
            {
                { "Id", "$_id" },
                { "ProdutoId", 1 },
                { "NomeProduto", "$produto.Nome" },
                { "CodigoInterno", 1 },
                { "QuantidadeAtual", "$QuantidadeAtual.Value" },
                { "QuantidadeMinima", 1 },
                { "Custo", "$CustoUnitario.Valor" }
            })
        };

        var raw = await ItensEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var totalCount = await ItensEstoque.CountDocumentsAsync(match);

        var items = raw.Select(x =>
        {
            var produtoId = x["ProdutoId"].AsGuid;
            var quantidade = x["QuantidadeAtual"].ToInt32();
            var custo = x["Custo"].ToDecimal();
            var taxa = taxas.TryGetValue(produtoId, out var t) ? t : 0m;
            var diasAte = taxa > 0 ? (int?)Math.Floor(quantidade / taxa) : null;
            var coberturaSugerida = taxa > 0 ? (int)Math.Ceiling(taxa * 30) : x["QuantidadeMinima"].ToInt32() * 2;
            var qtdRepor = Math.Max(0, coberturaSugerida - quantidade);
            return new ReposicaoSugerida(
                ItemEstoqueId: x["Id"].AsGuid,
                ProdutoId: produtoId,
                NomeProduto: x["NomeProduto"].AsString,
                CodigoInterno: x["CodigoInterno"].AsString,
                QuantidadeAtual: quantidade,
                QuantidadeMinima: x["QuantidadeMinima"].ToInt32(),
                QuantidadeSugeridaReposicao: qtdRepor,
                VelocidadeSaidaDiaria: Math.Round(taxa, 2),
                DiasAteRuptura: diasAte,
                CustoEstimadoReposicao: Math.Round(qtdRepor * custo, 2));
        }).ToList();

        await SetCachedAsync(cacheKey, (items, (int)totalCount), ReposicaoTtl);
        return (items, (int)totalCount);
    }

    // Projecao de ruptura

    public async Task<(IReadOnlyList<ProjecaoRuptura> Items, int TotalCount)> GetProjecaoRupturaAsync(
        Guid empresaId, int diasHistorico = 30, int page = 1, int pageSize = 20)
    {
        var cacheKey = $"analytics:projecao:{empresaId}:{diasHistorico}:{page}:{pageSize}";
        var cached = await GetCachedAsync<(List<ProjecaoRuptura>, int)>(cacheKey);
        if (cached != default) return cached;

        var de = DateTime.UtcNow.AddDays(-diasHistorico);
        var ate = DateTime.UtcNow;
        var dias = Math.Max(1, diasHistorico);

        var taxasPipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "Tipo", TipoMovimentacaoEstoque.Saida },
                { "DataMovimentacao", new BsonDocument("$gte", de).Add("$lte", ate) }
            }),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$ProdutoId" },
                { "Total", new BsonDocument("$sum", "$Quantidade.Value") }
            })
        };

        var taxasRaw = await MovimentacoesEstoque.Aggregate<BsonDocument>(taxasPipeline).ToListAsync();
        var taxas = taxasRaw.ToDictionary(x => x["_id"].AsGuid, x => (decimal)x["Total"].ToInt32() / dias);

        var match = new BsonDocument
        {
            { "EmpresaId", empresaId },
            { "QuantidadeAtual.Value", new BsonDocument("$gt", 0) }
        };

        var pipeline = new[]
        {
            new BsonDocument("$match", match),
            new BsonDocument("$lookup", new BsonDocument
            {
                { "from", "Produtos" },
                { "localField", "ProdutoId" },
                { "foreignField", "_id" },
                { "as", "produto" }
            }),
            new BsonDocument("$unwind", "$produto"),
            new BsonDocument("$sort", new BsonDocument("ProdutoId", 1)),
            new BsonDocument("$skip", (page - 1) * pageSize),
            new BsonDocument("$limit", pageSize),
            new BsonDocument("$project", new BsonDocument
            {
                { "Id", "$_id" },
                { "ProdutoId", 1 },
                { "NomeProduto", "$produto.Nome" },
                { "CodigoInterno", 1 },
                { "QuantidadeAtual", "$QuantidadeAtual.Value" }
            })
        };

        var raw = await ItensEstoque.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var totalCount = await ItensEstoque.CountDocumentsAsync(match);
        var agora = DateTime.UtcNow;

        var items = raw.Select(x =>
        {
            var produtoId = x["ProdutoId"].AsGuid;
            var quantidade = x["QuantidadeAtual"].ToInt32();
            var taxa = taxas.TryGetValue(produtoId, out var t) ? t : 0m;
            var diasAte = taxa > 0 ? (int?)Math.Floor(quantidade / taxa) : null;
            return new ProjecaoRuptura(
                ItemEstoqueId: x["Id"].AsGuid,
                ProdutoId: produtoId,
                NomeProduto: x["NomeProduto"].AsString,
                CodigoInterno: x["CodigoInterno"].AsString,
                QuantidadeAtual: quantidade,
                TaxaSaidaDiaria: Math.Round(taxa, 2),
                DiasAteRuptura: diasAte,
                DataEstimadaRuptura: diasAte.HasValue ? agora.AddDays(diasAte.Value) : null);
        })
        .OrderBy(x => x.DiasAteRuptura ?? int.MaxValue)
        .ToList();

        await SetCachedAsync(cacheKey, (items, (int)totalCount), ProjecaoTtl);
        return (items, (int)totalCount);
    }

    // Vendas por canal

    public async Task<IReadOnlyList<VendaPorCanal>> GetVendasPorCanalAsync(Guid empresaId, DateTime de, DateTime ate)
    {
        var cacheKey = $"analytics:canal:{empresaId}:{de:yyyyMMdd}:{ate:yyyyMMdd}";
        var cached = await GetCachedAsync<List<VendaPorCanal>>(cacheKey);
        if (cached is not null) return cached;

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument
            {
                { "EmpresaId", empresaId },
                { "DataVenda", new BsonDocument("$gte", de).Add("$lte", ate) }
            }),
            new BsonDocument("$unwind", "$ItensVenda"),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$Canal" },
                { "TotalVendas", new BsonDocument("$sum", 1) },
                { "Receita", new BsonDocument("$sum", "$ValorTotal.Valor") },
                { "TotalItens", new BsonDocument("$sum", "$ItensVenda.Quantidade.Value") }
            })
        };

        var raw = await Vendas.Aggregate<BsonDocument>(pipeline).ToListAsync();
        var totalReceita = raw.Sum(x => x["Receita"].ToDecimal());

        var result = raw.Select(x => new VendaPorCanal(
            Canal: (CanalVenda)x["_id"].AsInt32,
            TotalVendas: x["TotalVendas"].ToInt32(),
            TotalItensVendidos: x["TotalItens"].ToInt32(),
            ReceitaTotal: Math.Round(x["Receita"].ToDecimal(), 2),
            TicketMedio: x["TotalVendas"].ToInt32() > 0 ? Math.Round(x["Receita"].ToDecimal() / x["TotalVendas"].ToInt32(), 2) : 0m,
            PercentualReceita: totalReceita > 0 ? Math.Round(x["Receita"].ToDecimal() / totalReceita * 100m, 2) : 0m))
            .OrderByDescending(x => x.ReceitaTotal)
            .ToList();

        await SetCachedAsync(cacheKey, result, CanalTtl);
        return result;
    }
}
