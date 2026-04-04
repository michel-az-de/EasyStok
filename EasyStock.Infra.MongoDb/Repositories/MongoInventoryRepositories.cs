using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class ItemEstoqueRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IItemEstoqueRepository
{
    private IMongoCollection<ItemEstoque> Collection => Context.GetCollection<ItemEstoque>(MongoCollectionNames.ItensEstoque);
    private IMongoCollection<Produto> Produtos => Context.GetCollection<Produto>(MongoCollectionNames.Produtos);
    private IMongoCollection<ProdutoVariacao> Variacoes => Context.GetCollection<ProdutoVariacao>(MongoCollectionNames.ProdutosVariacao);

    public Task<ItemEstoque?> GetByIdAsync(Guid id) =>
        Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public Task<ItemEstoque?> GetByIdAsync(Guid empresaId, Guid id) =>
        Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<ItemEstoque>> SearchAsync(Guid empresaId, string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return [];

        var regex = new MongoDB.Bson.BsonRegularExpression(BuildContainsPattern(termo), "i");
        var filter = Builders<ItemEstoque>.Filter.And(
            Builders<ItemEstoque>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ItemEstoque>.Filter.Or(
                Builders<ItemEstoque>.Filter.Regex(x => x.CodigoInterno, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.CodigoMarketplace, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.ChavePesquisa, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.VariacaoDescricao, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.Cor, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.Tamanho, regex),
                Builders<ItemEstoque>.Filter.Regex(x => x.DescricaoAnuncio, regex)));

        return await Collection.Find(filter).Limit(100).ToListAsync();
    }

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetEstoqueBaixoAsync(Guid empresaId, int limite, int page = 1, int pageSize = 20) =>
        PaginateAsync(Builders<ItemEstoque>.Filter.Where(x => x.EmpresaId == empresaId && x.QuantidadeAtual.Value <= limite),
            Builders<ItemEstoque>.Sort.Ascending("QuantidadeAtual"),
            page,
            pageSize);

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetProximoVencimentoAsync(Guid empresaId, int dias, int page = 1, int pageSize = 20)
    {
        var cutoff = DateTime.UtcNow.AddDays(dias);
        return PaginateAsync(Builders<ItemEstoque>.Filter.Where(x =>
            x.EmpresaId == empresaId &&
            x.ValidadeEm != null &&
            x.ValidadeEm.DataValidade <= cutoff),
            Builders<ItemEstoque>.Sort.Ascending("ValidadeEm"),
            page,
            pageSize);
    }

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensParadosAsync(Guid empresaId, int diasSemMovimento, int page = 1, int pageSize = 20)
    {
        var cutoff = DateTime.UtcNow.AddDays(-diasSemMovimento);
        return PaginateAsync(Builders<ItemEstoque>.Filter.Where(x =>
            x.EmpresaId == empresaId &&
            (x.UltimaMovimentacaoEm == null || x.UltimaMovimentacaoEm < cutoff)),
            Builders<ItemEstoque>.Sort.Descending(x => x.UltimaMovimentacaoEm),
            page,
            pageSize);
    }

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetSugestaoReposicaoAsync(Guid empresaId, int limiteQuantidade = 5, int page = 1, int pageSize = 20) =>
        PaginateAsync(Builders<ItemEstoque>.Filter.Where(x => x.EmpresaId == empresaId && x.QuantidadeAtual.Value < limiteQuantidade),
            Builders<ItemEstoque>.Sort.Ascending("QuantidadeAtual"),
            page,
            pageSize);

    public Task<(IEnumerable<ItemEstoque> Items, int TotalCount)> GetItensEstoquePaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20) =>
        PaginateAsync(Builders<ItemEstoque>.Filter.Eq(x => x.EmpresaId, empresaId), Builders<ItemEstoque>.Sort.Ascending(x => x.ProdutoId), page, pageSize);

    public async Task<(int QuantidadeEmEstoque, decimal ValorTotalEstoque, decimal TicketMedioSugerido)> GetResumoEstoqueAsync(Guid empresaId)
    {
        var items = await Collection.Find(x => x.EmpresaId == empresaId).ToListAsync();
        if (items.Count == 0) return (0, 0m, 0m);

        return (
            items.Sum(x => x.QuantidadeAtual.Value),
            items.Sum(x => x.CustoUnitario.Valor * x.QuantidadeAtual.Value),
            items.Average(x => x.PrecoVendaSugerido?.Valor ?? x.CustoUnitario.Valor * 1.3m));
    }

    public async Task<ItemEstoque?> GetItemComProdutoAsync(Guid empresaId, Guid id)
    {
        var item = await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();
        if (item is null) return null;

        item.Produto = await Produtos.Find(x => x.Id == item.ProdutoId).FirstOrDefaultAsync();
        if (item.ProdutoVariacaoId.HasValue)
            item.ProdutoVariacao = await Variacoes.Find(x => x.Id == item.ProdutoVariacaoId.Value).FirstOrDefaultAsync();

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
        EnqueueReplace(Collection, itemEstoque.Id, itemEstoque);
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

    public async Task<(IEnumerable<MovimentacaoEstoque> Items, int TotalCount)> GetByEmpresaAsync(Guid empresaId, DateTime? de = null, DateTime? ate = null, TipoMovimentacaoEstoque? tipo = null, int page = 1, int pageSize = 20)
    {
        var items = await Collection.Find(x => x.EmpresaId == empresaId).ToListAsync();
        var filtered = items.Where(x =>
                (!de.HasValue || x.DataMovimentacao >= de.Value) &&
                (!ate.HasValue || x.DataMovimentacao <= ate.Value) &&
                (!tipo.HasValue || x.Tipo == tipo.Value))
            .OrderByDescending(x => x.DataMovimentacao)
            .ToList();

        return (filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), filtered.Count);
    }

    public async Task<IEnumerable<MovimentacaoEstoque>> GetByItemEstoqueAsync(Guid itemEstoqueId) =>
        await Collection.Find(x => x.ItemEstoqueId == itemEstoqueId)
            .SortByDescending(x => x.DataMovimentacao)
            .ToListAsync();

    public async Task<decimal> GetTaxaSaidaDiariaAsync(Guid empresaId, Guid? produtoId, DateTime de, DateTime ate)
    {
        var items = await Collection.Find(x =>
            x.EmpresaId == empresaId &&
            x.Tipo == TipoMovimentacaoEstoque.Saida &&
            x.DataMovimentacao >= de &&
            x.DataMovimentacao <= ate &&
            (!produtoId.HasValue || x.ProdutoId == produtoId.Value)).ToListAsync();

        var dias = Math.Max(1, (ate - de).Days);
        return items.Sum(x => x.Quantidade.Value) / (decimal)dias;
    }

    public async Task<IReadOnlyDictionary<Guid, decimal>> GetTaxaSaidaDiariaPorProdutoAsync(Guid empresaId, IEnumerable<Guid> produtoIds, DateTime de, DateTime ate)
    {
        var ids = produtoIds.Distinct().ToHashSet();
        if (ids.Count == 0) return new Dictionary<Guid, decimal>();

        var items = await Collection.Find(x =>
            x.EmpresaId == empresaId &&
            ids.Contains(x.ProdutoId) &&
            x.Tipo == TipoMovimentacaoEstoque.Saida &&
            x.DataMovimentacao >= de &&
            x.DataMovimentacao <= ate).ToListAsync();

        var dias = Math.Max(1, (ate - de).Days);
        return items.GroupBy(x => x.ProdutoId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.Quantidade.Value) / (decimal)dias);
    }

    public async Task<IEnumerable<(int Ano, int Mes, int TotalSaidas, decimal ValorTotal)>> GetAgregacaoMensalAsync(Guid empresaId, Guid produtoId, int meses = 12)
    {
        var de = DateTime.UtcNow.AddMonths(-meses);
        var items = await Collection.Find(x =>
            x.EmpresaId == empresaId &&
            x.ProdutoId == produtoId &&
            x.Tipo == TipoMovimentacaoEstoque.Saida &&
            x.DataMovimentacao >= de).ToListAsync();

        return items.GroupBy(x => new { x.DataMovimentacao.Year, x.DataMovimentacao.Month })
            .Select(g => (g.Key.Year, g.Key.Month, g.Sum(x => x.Quantidade.Value), g.Sum(x => x.ValorTotal?.Valor ?? 0m)))
            .OrderBy(x => x.Year)
            .ThenBy(x => x.Month)
            .Select(x => (x.Year, x.Month, x.Item3, x.Item4))
            .ToList();
    }
}

public sealed class VendaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IVendaRepository
{
    private IMongoCollection<Venda> Collection => Context.GetCollection<Venda>(MongoCollectionNames.Vendas);

    public Task<Venda?> GetByIdAsync(Guid id) =>
        Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public Task<Venda?> GetByIdAsync(Guid empresaId, Guid id) =>
        Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

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
}

public sealed class NotificacaoRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), INotificacaoRepository
{
    private IMongoCollection<Notificacao> Collection => Context.GetCollection<Notificacao>(MongoCollectionNames.Notificacoes);

    public Task<Notificacao?> GetByIdAsync(Guid id) =>
        Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<(IEnumerable<Notificacao> Items, int TotalCount)> GetByEmpresaAsync(Guid empresaId, bool? lida = null, int page = 1, int pageSize = 20)
    {
        var items = await Collection.Find(x => x.EmpresaId == empresaId).ToListAsync();
        var filtered = items.Where(x => !lida.HasValue || x.Lida == lida.Value)
            .OrderByDescending(x => x.CriadaEm)
            .ToList();

        return (filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(), filtered.Count);
    }

    public Task<bool> ExisteNotificacaoNaoLidaAsync(Guid empresaId, TipoAlertaEstoque tipo, Guid referenciaId) =>
        Collection.Find(x => x.EmpresaId == empresaId && x.TipoAlerta == tipo && x.ReferenciaId == referenciaId && !x.Lida).AnyAsync();

    public Task AddAsync(Notificacao notificacao)
    {
        EnqueueInsert(Collection, notificacao);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Notificacao notificacao)
    {
        EnqueueReplace(Collection, notificacao.Id, notificacao);
        return Task.CompletedTask;
    }

    public Task MarcarTodasComoLidasAsync(Guid empresaId) =>
        Collection.UpdateManyAsync(
            Builders<Notificacao>.Filter.Where(x => x.EmpresaId == empresaId && !x.Lida),
            Builders<Notificacao>.Update
                .Set(x => x.Lida, true)
                .Set(x => x.LidaEm, DateTime.UtcNow));
}
