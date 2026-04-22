using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class ItemEstoqueRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IItemEstoqueRepository
{
    private IMongoCollection<ItemEstoque> Collection => Context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque);
    private IMongoCollection<Produto> Produtos => Context.GetCollection<Produto>(MongoCollectionNames.Produtos);
    private IMongoCollection<ProdutoVariacao> Variacoes => Context.GetCollection<ProdutoVariacao>(MongoCollectionNames.ProdutosVariacao);

    public async Task<ItemEstoque?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<ItemEstoque?> GetByIdAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo, int maxResults = 100)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return [];

        termo = termo.Trim();
        var regex = new BsonRegularExpression(BuildContainsPattern(termo), "i");
        var filter = Builders<ItemEstoque>.Filter.And(
            Builders<ItemEstoque>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ItemEstoque>.Filter.Or(
                Builders<ItemEstoque>.Filter.Text(termo),
                Builders<ItemEstoque>.Filter.Regex(x => x.CodigoInterno, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.CodigoMarketplace, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.ChavePesquisa, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.VariacaoDescricao, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.Cor, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.Tamanho, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.DescricaoAnuncio, regex)));

        return await Collection.Find(filter).Limit(maxResults).ToListAsync();
    }

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetEstoqueBaixoAsync(Guid empresaId, int limite, int page = 1, int pageSize = 20, Guid? lojaId = null) =>
        PaginateAsync(Builders<ItemEstoque>.Filter.Where(x => x.EmpresaId == empresaId && x.QuantidadeAtual.Value <= limite &&
            (!lojaId.HasValue || x.LojaId == lojaId.Value)),
            Builders<ItemEstoque>.Sort.Ascending("QuantidadeAtual"),
            page,
            pageSize);

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20, Guid? lojaId = null)
    {
        var cutoff = DateTime.UtcNow.AddDays(dias);
        return PaginateAsync(Builders<ItemEstoque>.Filter.Where(x =>
            x.EmpresaId == empresaId &&
            x.ValidadeEm != null &&
            x.ValidadeEm.DataValidade <= cutoff &&
            (!lojaId.HasValue || x.LojaId == lojaId.Value)),
            Builders<ItemEstoque>.Sort.Ascending("ValidadeEm"),
            page,
            pageSize);
    }

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20, Guid? lojaId = null)
    {
        var cutoff = DateTime.UtcNow.AddDays(-diasSemMovimento);
        return PaginateAsync(Builders<ItemEstoque>.Filter.Where(x =>
            x.EmpresaId == empresaId &&
            (x.UltimaMovimentacaoEm == null || x.UltimaMovimentacaoEm < cutoff) &&
            (!lojaId.HasValue || x.LojaId == lojaId.Value)),
            Builders<ItemEstoque>.Sort.Descending(x => x.UltimaMovimentacaoEm),
            page,
            pageSize);
    }

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetSugestaoReposicaoAsync(Guid empresaId, int limiteQuantidade = 5, int page = 1, int pageSize = 20, Guid? lojaId = null) =>
        PaginateAsync(Builders<ItemEstoque>.Filter.Where(x => x.EmpresaId == empresaId && x.QuantidadeAtual.Value < limiteQuantidade &&
            (!lojaId.HasValue || x.LojaId == lojaId.Value)),
            Builders<ItemEstoque>.Sort.Ascending("QuantidadeAtual"),
            page,
            pageSize);

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20) =>
        PaginateAsync(Builders<ItemEstoque>.Filter.Eq(x => x.EmpresaId, empresaId), Builders<ItemEstoque>.Sort.Ascending(x => x.ProdutoId), page, pageSize);

    public async Task<(int QuantidadeEmEstoque, decimal ValorTotalEstoque, decimal TicketMedioSugerido)> GetResumoEstoqueAsync(Guid empresaId)
    {
        var resumo = await Collection.Aggregate()
            .Match(x => x.EmpresaId == empresaId)
            .Group(new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "QuantidadeEmEstoque", new BsonDocument("$sum", "$QuantidadeAtual") },
                { "ValorTotalEstoque", new BsonDocument("$sum", new BsonDocument("$multiply", new BsonArray { "$QuantidadeAtual", "$CustoUnitario" })) },
                { "TicketMedioSugerido", new BsonDocument("$avg", new BsonDocument("$ifNull", new BsonArray { "$PrecoVendaSugerido", new BsonDocument("$multiply", new BsonArray { "$CustoUnitario", 1.3m }) })) }
            })
            .FirstOrDefaultAsync();

        if (resumo is null)
            return (0, 0m, 0m);

        return (
            resumo["QuantidadeEmEstoque"].ToInt32(),
            resumo["ValorTotalEstoque"].ToDecimal(),
            resumo["TicketMedioSugerido"].ToDecimal());
    }

    public async Task<IReadOnlyCollection<ItemEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId)
            .SortByDescending(x => x.EntradaEm)
            .ToListAsync();

    public async Task<IReadOnlyCollection<ItemEstoque>> GetLotesDisponiveisParaSaidaAsync(Guid empresaId, Guid produtoId, Guid? produtoVariacaoId)
    {
        var filter = Builders<ItemEstoque>.Filter.And(
            Builders<ItemEstoque>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ItemEstoque>.Filter.Eq(x => x.ProdutoId, produtoId),
            Builders<ItemEstoque>.Filter.Gt("QuantidadeAtual", 0),
            produtoVariacaoId.HasValue
                ? Builders<ItemEstoque>.Filter.Eq(x => x.ProdutoVariacaoId, produtoVariacaoId.Value)
                : Builders<ItemEstoque>.Filter.Eq(x => x.ProdutoVariacaoId, null));

        return await Collection.Find(filter)
            .Sort(Builders<ItemEstoque>.Sort.Ascending(x => x.EntradaEm).Ascending(x => x.CriadoEm))
            .ToListAsync();
    }

    public Task<bool> ExisteEstoqueDoProdutoAsync(Guid empresaId, Guid produtoId) =>
        Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId && x.QuantidadeAtual.Value > 0).AnyAsync();

    public Task<bool> ExisteEstoqueDaVariacaoAsync(Guid empresaId, Guid produtoId, Guid variacaoId) =>
        Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId && x.ProdutoVariacaoId == variacaoId && x.QuantidadeAtual.Value > 0).AnyAsync();

    public async Task<ItemEstoque?> GetItemComProdutoAsync(Guid empresaId, Guid id)
    {
        var item = await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();
        if (item is null) return null;

        item.Produto = await Produtos.Find(x => x.EmpresaId == empresaId && x.Id == item.ProdutoId).FirstOrDefaultAsync();
        if (item.ProdutoVariacaoId.HasValue)
            item.ProdutoVariacao = await Variacoes.Find(x => x.EmpresaId == empresaId && x.Id == item.ProdutoVariacaoId.Value).FirstOrDefaultAsync();

        return item;
    }

    public Task InsertAsync(ItemEstoque itemEstoque)
    {
        EnqueueInsert(Collection, itemEstoque);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ItemEstoque itemEstoque)
    {
        itemEstoque.Produto = null;
        itemEstoque.ProdutoVariacao = null;
        EnqueueReplaceScoped(Collection, itemEstoque.Id, itemEstoque.EmpresaId, itemEstoque);
        return Task.CompletedTask;
    }

    public Task UpdateRangeAsync(IEnumerable<ItemEstoque> itensEstoque)
    {
        foreach (var item in itensEstoque)
        {
            item.Produto = null;
            item.ProdutoVariacao = null;
            EnqueueReplaceScoped(Collection, item.Id, item.EmpresaId, item);
        }
        return Task.CompletedTask;
    }

    private async Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> PaginateAsync(
        FilterDefinition<ItemEstoque> filter,
        SortDefinition<ItemEstoque> sort,
        int page,
        int pageSize)
    {
        var total = (int)await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .Sort(sort)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }
}

public sealed class MovimentacaoEstoqueRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IMovimentacaoEstoqueRepository
{
    private IMongoCollection<MovimentacaoEstoque> Collection => Context.GetCollection<MovimentacaoEstoque>(MongoCollectionNames.MovimentacoesEstoque);

    public Task InsertAsync(MovimentacaoEstoque movimentacao)
    {
        EnqueueInsert(Collection, movimentacao);
        return Task.CompletedTask;
    }

    public Task InsertRangeAsync(IEnumerable<MovimentacaoEstoque> movimentacoes)
    {
        foreach (var m in movimentacoes) EnqueueInsert(Collection, m);
        return Task.CompletedTask;
    }

    public async Task<MovimentacaoEstoque?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    // MongoDB não suporta FOR UPDATE; lock implementado apenas em PostgreSQL
    public Task<MovimentacaoEstoque?> GetByIdComLockAsync(Guid id) => GetByIdAsync(id);

    public Task UpdateAsync(MovimentacaoEstoque movimentacao)
    {
        EnqueueReplaceScoped(Collection, movimentacao.Id, movimentacao.EmpresaId, movimentacao);
        return Task.CompletedTask;
    }

    public async Task<(IEnumerable<MovimentacaoEstoque> Items, int TotalCount)> GetByEmpresaAsync(Guid empresaId, DateTime? de = null, DateTime? ate = null, TipoMovimentacaoEstoque? tipo = null, NaturezaMovimentacaoEstoque? natureza = null, int page = 1, int pageSize = 20)
    {
        var filter = Builders<MovimentacaoEstoque>.Filter.Eq(x => x.EmpresaId, empresaId);

        if (de.HasValue)
            filter &= Builders<MovimentacaoEstoque>.Filter.Gte(x => x.DataMovimentacao, de.Value);
        if (ate.HasValue)
            filter &= Builders<MovimentacaoEstoque>.Filter.Lte(x => x.DataMovimentacao, ate.Value);
        if (tipo.HasValue)
            filter &= Builders<MovimentacaoEstoque>.Filter.Eq(x => x.Tipo, tipo.Value);
        if (natureza.HasValue)
            filter &= Builders<MovimentacaoEstoque>.Filter.Eq(x => x.Natureza, natureza.Value);

        var total = (int)await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .SortByDescending(x => x.DataMovimentacao)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<IEnumerable<MovimentacaoEstoque>> GetByItemEstoqueAsync(Guid empresaId, Guid itemEstoqueId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ItemEstoqueId == itemEstoqueId)
            .SortByDescending(x => x.DataMovimentacao)
            .ToListAsync();

    public async Task<IEnumerable<MovimentacaoEstoque>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId)
            .SortByDescending(x => x.DataMovimentacao)
            .ToListAsync();

    public async Task<decimal> GetTaxaSaidaDiariaAsync(Guid empresaId, Guid? produtoId, DateTime de, DateTime ate)
    {
        var totalSaidas = await Collection.Aggregate()
            .Match(x =>
            x.EmpresaId == empresaId &&
            x.Tipo == TipoMovimentacaoEstoque.Saida &&
            x.DataMovimentacao >= de &&
            x.DataMovimentacao <= ate &&
            (!produtoId.HasValue || x.ProdutoId == produtoId.Value))
            .Group(new BsonDocument
            {
                { "_id", BsonNull.Value },
                { "TotalSaidas", new BsonDocument("$sum", "$Quantidade") }
            })
            .FirstOrDefaultAsync();

        var dias = Math.Max(1, (ate - de).Days);
        return totalSaidas is null ? 0m : totalSaidas["TotalSaidas"].ToDecimal() / dias;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetTaxaSaidaDiariaPorProdutoAsync(Guid empresaId, IEnumerable<Guid> produtoIds, DateTime de, DateTime ate)
    {
        var ids = produtoIds.Distinct().ToHashSet();
        if (ids.Count == 0) return new Dictionary<Guid, decimal>();

        var dias = Math.Max(1, (ate - de).Days);
        var items = await Collection.Aggregate()
            .Match(x =>
                x.EmpresaId == empresaId &&
                ids.Contains(x.ProdutoId) &&
                x.Tipo == TipoMovimentacaoEstoque.Saida &&
                x.DataMovimentacao >= de &&
                x.DataMovimentacao <= ate)
            .Group(new BsonDocument
            {
                { "_id", "$ProdutoId" },
                { "TotalSaidas", new BsonDocument("$sum", "$Quantidade") }
            })
            .ToListAsync();

        return items.ToDictionary(
            x => x["_id"].AsGuid,
            x => x["TotalSaidas"].ToDecimal() / dias);
    }

    public async Task<IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)>> GetAgregacaoMensalAsync(Guid empresaId, Guid produtoId, int meses = 12)
    {
        var de = DateTime.UtcNow.AddMonths(-meses);
        var items = await Collection.Aggregate()
            .Match(x =>
                x.EmpresaId == empresaId &&
                x.ProdutoId == produtoId &&
                x.Tipo == TipoMovimentacaoEstoque.Saida &&
                x.DataMovimentacao >= de)
            .Group(new BsonDocument
            {
                {
                    "_id", new BsonDocument
                    {
                        { "Ano", new BsonDocument("$year", "$DataMovimentacao") },
                        { "Mes", new BsonDocument("$month", "$DataMovimentacao") }
                    }
                },
                { "TotalSaidas", new BsonDocument("$sum", "$Quantidade") },
                { "ValorTotal", new BsonDocument("$sum", new BsonDocument("$ifNull", new BsonArray { "$ValorTotal", 0m })) }
            })
            .Sort(new BsonDocument("_id.Ano", 1).Add("_id.Mes", 1))
            .ToListAsync();

        return items.Select(x => (
            x["_id"]["Ano"].ToInt32(),
            x["_id"]["Mes"].ToInt32(),
            x["TotalSaidas"].ToInt32(),
            x["ValorTotal"].ToDecimal()))
            .ToList();
    }

    public async Task<KpisMovimentacao> GetKpisAsync(Guid empresaId, DateTime? de = null, DateTime? ate = null, TipoMovimentacaoEstoque? tipo = null, NaturezaMovimentacaoEstoque? natureza = null)
    {
        var filter = Builders<MovimentacaoEstoque>.Filter.Eq(x => x.EmpresaId, empresaId);
        if (de.HasValue) filter &= Builders<MovimentacaoEstoque>.Filter.Gte(x => x.DataMovimentacao, de.Value);
        if (ate.HasValue) filter &= Builders<MovimentacaoEstoque>.Filter.Lte(x => x.DataMovimentacao, ate.Value);
        if (tipo.HasValue) filter &= Builders<MovimentacaoEstoque>.Filter.Eq(x => x.Tipo, tipo.Value);
        if (natureza.HasValue) filter &= Builders<MovimentacaoEstoque>.Filter.Eq(x => x.Natureza, natureza.Value);

        var all = await Collection.Find(filter).ToListAsync();
        return new KpisMovimentacao(
            all.Sum(m => m.Quantidade.Value),
            all.Where(m => m.ValorTotal != null).Sum(m => m.ValorTotal!.Valor),
            all.Count(m => m.Natureza == NaturezaMovimentacaoEstoque.Venda),
            all.Count(m => m.Natureza == NaturezaMovimentacaoEstoque.Perda));
    }

    public async Task<IEnumerable<MovimentacaoEstoque>> SearchAsync(Guid empresaId, string termo, int maxResults = 20)
    {
        var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(termo.Trim()), "i");
        return await Collection.Find(
            Builders<MovimentacaoEstoque>.Filter.Eq(x => x.EmpresaId, empresaId) &
            Builders<MovimentacaoEstoque>.Filter.Or(
                Builders<MovimentacaoEstoque>.Filter.Regex(x => x.Descricao, regex),
                Builders<MovimentacaoEstoque>.Filter.Regex(x => x.DocumentoReferencia, regex)))
            .SortByDescending(x => x.DataMovimentacao)
            .Limit(maxResults).ToListAsync();
    }
}

public sealed class VendaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IVendaRepository
{
    private IMongoCollection<Venda> Collection => Context.GetCollection<Venda>(MongoCollectionNames.Vendas);

    public async Task<Venda?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<Venda?> GetByIdAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<(IEnumerable<Venda> Vendas, int TotalCount)> GetVendasPorEmpresaAsync(Guid empresaId, int page = 1, int pageSize = 20)
    {
        var filter = Builders<Venda>.Filter.Eq(x => x.EmpresaId, empresaId);
        var total = (int)await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .SortByDescending(x => x.DataVenda)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task InsertAsync(Venda venda)
    {
        EnqueueInsert(Collection, venda);
        return Task.CompletedTask;
    }
}

public sealed class ItemVendaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IItemVendaRepository
{
    private IMongoCollection<ItemVenda> Collection => Context.GetCollection<ItemVenda>(MongoCollectionNames.ItensVenda);

    public Task InsertAsync(ItemVenda itemVenda)
    {
        itemVenda.Produto = null;
        EnqueueInsert(Collection, itemVenda);
        return Task.CompletedTask;
    }

    public Task InsertRangeAsync(IEnumerable<ItemVenda> itens)
    {
        foreach (var iv in itens) { iv.Produto = null; EnqueueInsert(Collection, iv); }
        return Task.CompletedTask;
    }
}

public sealed class NotificacaoRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), INotificacaoRepository
{
    private IMongoCollection<Notificacao> Collection => Context.GetCollection<Notificacao>(MongoCollectionNames.Notificacoes);

    public async Task<Notificacao?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<(IEnumerable<Notificacao> Items, int TotalCount)> GetByEmpresaAsync(Guid empresaId, bool? lida = null, TipoAlertaEstoque? tipo = null, SeveridadeNotificacao? severidade = null, int page = 1, int pageSize = 20)
    {
        var filter = Builders<Notificacao>.Filter.Eq(x => x.EmpresaId, empresaId);
        if (lida.HasValue)
            filter &= Builders<Notificacao>.Filter.Eq(x => x.Lida, lida.Value);
        if (tipo.HasValue)
            filter &= Builders<Notificacao>.Filter.Eq(x => x.TipoAlerta, tipo.Value);
        if (severidade.HasValue)
            filter &= Builders<Notificacao>.Filter.Eq(x => x.Severidade, severidade.Value);

        var total = (int)await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .SortBy(x => x.Severidade)
            .ThenByDescending(x => x.CriadaEm)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<IEnumerable<Notificacao>> GetRecentesNaoLidasAsync(Guid empresaId, int limit = 5)
    {
        return await Collection.Find(x => x.EmpresaId == empresaId && !x.Lida)
            .SortBy(x => x.Severidade)
            .ThenByDescending(x => x.CriadaEm)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<NotificacaoResumo> GetResumoAsync(Guid empresaId)
    {
        var naoLidas = await Collection.Find(x => x.EmpresaId == empresaId && !x.Lida).ToListAsync();
        var porTipo = naoLidas.GroupBy(n => n.TipoAlerta.ToString()).ToDictionary(g => g.Key, g => g.Count());
        return new NotificacaoResumo
        {
            TotalNaoLidas = naoLidas.Count,
            Criticas = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Critica),
            Altas = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Alta),
            Medias = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Media),
            Informativas = naoLidas.Count(n => n.Severidade == SeveridadeNotificacao.Informativa),
            PorTipo = porTipo
        };
    }

    public async Task<bool> ExisteNotificacaoNaoLidaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid referenciaId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.TipoAlerta == tipo && x.ReferenciaId == referenciaId && !x.Lida).AnyAsync();

    public Task<bool> ExisteNotificacaoDoDiaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid? referenciaId, DateTime dataReferencia)
    {
        var inicio = dataReferencia.Date;
        var fim = inicio.AddDays(1);
        return Collection.Find(x => x.EmpresaId == empresaId &&
                                    x.TipoAlerta == tipo &&
                                    x.ReferenciaId == referenciaId &&
                                    x.CriadaEm >= inicio &&
                                    x.CriadaEm < fim).AnyAsync();
    }

    public async Task<int> CountNaoLidasAsync(Guid empresaId) =>
        (int)await Collection.CountDocumentsAsync(x => x.EmpresaId == empresaId && !x.Lida);

    public Task AddAsync(Notificacao notificacao)
    {
        EnqueueInsert(Collection, notificacao);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Notificacao notificacao)
    {
        EnqueueReplaceScoped(Collection, notificacao.Id, notificacao.EmpresaId, notificacao);
        return Task.CompletedTask;
    }

    public Task MarcarTodasComoLidasAsync(Guid empresaId)
    {
        var agora = DateTime.UtcNow;
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? Collection.UpdateManyAsync(
                    Builders<Notificacao>.Filter.Where(x => x.EmpresaId == empresaId && !x.Lida),
                    Builders<Notificacao>.Update
                        .Set(x => x.Lida, true)
                        .Set(x => x.LidaEm, agora),
                    cancellationToken: ct)
                : Collection.UpdateManyAsync(
                    session,
                    Builders<Notificacao>.Filter.Where(x => x.EmpresaId == empresaId && !x.Lida),
                    Builders<Notificacao>.Update
                        .Set(x => x.Lida, true)
                        .Set(x => x.LidaEm, agora),
                    cancellationToken: ct));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid empresaId, Guid id)
    {
        EnqueueDeleteScoped(Collection, id, empresaId);
        return Task.CompletedTask;
    }
}
