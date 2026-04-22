using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class CategoriaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), ICategoriaRepository
{
    private IMongoCollection<Categoria> Collection => Context.GetCollection<Categoria>(MongoCollectionNames.Categorias);
    private IMongoCollection<Produto> Produtos => Context.GetCollection<Produto>(MongoCollectionNames.Produtos);

    public async Task<Categoria?> GetByIdAsync(Guid id)
    {
        var categoria = await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (categoria is null) return null;

        categoria.SubCategorias = await Collection.Find(x => x.CategoriaPaiId == id).ToListAsync();
        return categoria;
    }

    public async Task<IEnumerable<Categoria>> GetByEmpresaAsync(Guid empresaId)
    {
        var categorias = await Collection.Find(x => x.EmpresaId == empresaId)
            .SortByDescending(x => x.CriadoEm)
            .ThenByDescending(x => x.Id)
            .ToListAsync();
        var lookup = categorias.ToLookup(x => x.CategoriaPaiId);

        foreach (var categoria in categorias)
            categoria.SubCategorias = lookup[categoria.Id]
                .OrderByDescending(x => x.CriadoEm)
                .ThenByDescending(x => x.Id)
                .ToList();

        return categorias.Where(x => x.CategoriaPaiId is null).ToList();
    }

    public Task<bool> ExisteProdutosNaCategoriaAsync(Guid categoriaId) =>
        Produtos.Find(x => x.CategoriaId == categoriaId).AnyAsync();

    public Task AddAsync(Categoria categoria)
    {
        EnqueueInsert(Collection, categoria);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Categoria categoria)
    {
        EnqueueReplaceScoped(Collection, categoria.Id, categoria.EmpresaId, categoria);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid empresaId, Guid id)
    {
        EnqueueDeleteScoped(Collection, id, empresaId);
        return Task.CompletedTask;
    }
}

public sealed class EmpresaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IEmpresaRepository
{
    private IMongoCollection<Empresa> Collection => Context.GetCollection<Empresa>(MongoCollectionNames.Empresas);

    public async Task<Empresa?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<Empresa>> GetAllAsync() =>
        await Collection.Find(FilterDefinition<Empresa>.Empty).ToListAsync();

    public Task AddAsync(Empresa empresa)
    {
        EnqueueInsert(Collection, empresa);
        return Task.CompletedTask;
    }
}

public sealed class ProdutoRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoRepository
{
    private IMongoCollection<Produto> Collection => Context.GetCollection<Produto>(MongoCollectionNames.Produtos);

    public async Task<Produto?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<Produto?> GetByIdAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<Produto?> GetDetalheAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public Task<bool> ExistsSkuBaseAsync(Guid empresaId, string skuBase, Guid? ignoreProdutoId = null)
    {
        skuBase = skuBase.Trim();

        var filter = Builders<Produto>.Filter.And(
            Builders<Produto>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<Produto>.Filter.Eq("SkuBase", skuBase),
            ignoreProdutoId.HasValue
                ? Builders<Produto>.Filter.Ne(x => x.Id, ignoreProdutoId.Value)
                : Builders<Produto>.Filter.Empty);

        return Collection.Find(filter).AnyAsync();
    }

    public async Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo, int maxResults = 100)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return [];

        termo = termo.Trim();
        var pattern = BuildContainsPattern(termo);
        var regex = new MongoDB.Bson.BsonRegularExpression(pattern, "i");
        var filter = Builders<Produto>.Filter.And(
            Builders<Produto>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<Produto>.Filter.Or(
                Builders<Produto>.Filter.Text(termo),
                Builders<Produto>.Filter.Regex(x => x.Nome, regex),
                Builders<Produto>.Filter.Regex(x => x.Marca, regex),
                Builders<Produto>.Filter.Regex(x => x.DescricaoBase, regex),
                Builders<Produto>.Filter.Regex("SkuBase", regex),
                Builders<Produto>.Filter.Regex(x => x.CodigoBarras, regex)));

        return await Collection.Find(filter).Limit(maxResults).ToListAsync();
    }

    public async Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(
        Guid empresaId, int page = 1, int pageSize = 20, string? sort = "nome", string? order = "asc")
    {
        var filter = Builders<Produto>.Filter.Eq(x => x.EmpresaId, empresaId);
        var total = (int)await Collection.CountDocumentsAsync(filter);

        var items = await Collection.Find(filter)
            .SortByDescending(x => x.AlteradoEm)
            .ThenByDescending(x => x.CriadoEm)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public async Task<IReadOnlyList<string>> GetMarcasAsync(Guid empresaId, string? filtro = null, int max = 20)
    {
        var filter = Builders<Produto>.Filter.And(
            Builders<Produto>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<Produto>.Filter.Ne(x => x.Marca, (string?)null),
            Builders<Produto>.Filter.Ne(x => x.Marca, ""));

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            var regex = new MongoDB.Bson.BsonRegularExpression(filtro, "i");
            filter = Builders<Produto>.Filter.And(filter, Builders<Produto>.Filter.Regex(x => x.Marca, regex));
        }

        var marcas = await Collection.Find(filter)
            .Project(p => p.Marca!)
            .ToListAsync();

        return marcas.Where(m => !string.IsNullOrEmpty(m))
            .Distinct()
            .OrderBy(m => m)
            .Take(max)
            .ToList();
    }

    public Task InsertAsync(Produto produto)
    {
        EnqueueInsert(Collection, produto);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Produto produto)
    {
        EnqueueReplaceScoped(Collection, produto.Id, produto.EmpresaId, produto);
        return Task.CompletedTask;
    }
}

public sealed class ProdutoVariacaoRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoVariacaoRepository
{
    private IMongoCollection<ProdutoVariacao> Collection => Context.GetCollection<ProdutoVariacao>(MongoCollectionNames.ProdutosVariacao);

    public async Task<ProdutoVariacao?> GetByIdAsync(Guid id) =>
        await Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<ProdutoVariacao?> GetByIdAsync(Guid empresaId, Guid produtoId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId && x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<ProdutoVariacao>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId)
            .SortBy(x => x.Nome)
            .ToListAsync();

    public Task<bool> ExistsSkuAsync(Guid empresaId, string sku, Guid? ignoreVariacaoId = null)
    {
        sku = sku.Trim();

        var filter = Builders<ProdutoVariacao>.Filter.And(
            Builders<ProdutoVariacao>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ProdutoVariacao>.Filter.Eq("Sku", sku),
            ignoreVariacaoId.HasValue
                ? Builders<ProdutoVariacao>.Filter.Ne(x => x.Id, ignoreVariacaoId.Value)
                : Builders<ProdutoVariacao>.Filter.Empty);

        return Collection.Find(filter).AnyAsync();
    }

    public async Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo, int maxResults = 100)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return [];

        termo = termo.Trim();
        var pattern = BuildContainsPattern(termo);
        var regex = new MongoDB.Bson.BsonRegularExpression(pattern, "i");
        var filter = Builders<ProdutoVariacao>.Filter.And(
            Builders<ProdutoVariacao>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ProdutoVariacao>.Filter.Or(
                Builders<ProdutoVariacao>.Filter.Text(termo),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.Nome, regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.Cor, regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.Tamanho, regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.DescricaoComercial, regex),
                Builders<ProdutoVariacao>.Filter.Regex("Sku", regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.CodigoBarras, regex)));

        return await Collection.Find(filter).Limit(maxResults).ToListAsync();
    }

    public Task InsertAsync(ProdutoVariacao variacao)
    {
        EnqueueInsert(Collection, variacao);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProdutoVariacao variacao)
    {
        EnqueueReplaceScoped(Collection, variacao.Id, variacao.EmpresaId, variacao);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid empresaId, Guid id)
    {
        EnqueueDeleteScoped(Collection, id, empresaId);
        return Task.CompletedTask;
    }

    public Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId)
    {
        var filter = Builders<ProdutoVariacao>.Filter.And(
            Builders<ProdutoVariacao>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ProdutoVariacao>.Filter.Eq(x => x.ProdutoId, produtoId));
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? Collection.DeleteManyAsync(filter, ct)
                : Collection.DeleteManyAsync(session, filter, null, ct));
        return Task.CompletedTask;
    }
}

public sealed class ProdutoCaracteristicaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoCaracteristicaRepository
{
    private IMongoCollection<ProdutoCaracteristica> Collection => Context.GetCollection<ProdutoCaracteristica>(MongoCollectionNames.ProdutosCaracteristica);

    public async Task<IEnumerable<ProdutoCaracteristica>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId)
            .SortBy(x => x.OrdemExibicao)
            .ToListAsync();

    public Task InsertAsync(ProdutoCaracteristica caracteristica)
    {
        EnqueueInsert(Collection, caracteristica);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProdutoCaracteristica caracteristica)
    {
        EnqueueReplaceScoped(Collection, caracteristica.Id, caracteristica.EmpresaId, caracteristica);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid empresaId, Guid id)
    {
        EnqueueDeleteScoped(Collection, id, empresaId);
        return Task.CompletedTask;
    }

    public Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId)
    {
        var filter = Builders<ProdutoCaracteristica>.Filter.And(
            Builders<ProdutoCaracteristica>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ProdutoCaracteristica>.Filter.Eq(x => x.ProdutoId, produtoId));
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? Collection.DeleteManyAsync(filter, ct)
                : Collection.DeleteManyAsync(session, filter, null, ct));
        return Task.CompletedTask;
    }
}

public sealed class ProdutoEmbalagemRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoEmbalagemRepository
{
    private IMongoCollection<ProdutoEmbalagem> Collection => Context.GetCollection<ProdutoEmbalagem>(MongoCollectionNames.ProdutosEmbalagem);

    public async Task<IEnumerable<ProdutoEmbalagem>> GetByProdutoAsync(Guid empresaId, Guid produtoId) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId)
            .SortBy(x => x.Nome)
            .ToListAsync();

    public Task InsertAsync(ProdutoEmbalagem embalagem)
    {
        EnqueueInsert(Collection, embalagem);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ProdutoEmbalagem embalagem)
    {
        EnqueueReplaceScoped(Collection, embalagem.Id, embalagem.EmpresaId, embalagem);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid empresaId, Guid id)
    {
        EnqueueDeleteScoped(Collection, id, empresaId);
        return Task.CompletedTask;
    }

    public Task DeleteByProdutoAsync(Guid empresaId, Guid produtoId)
    {
        var filter = Builders<ProdutoEmbalagem>.Filter.And(
            Builders<ProdutoEmbalagem>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ProdutoEmbalagem>.Filter.Eq(x => x.ProdutoId, produtoId));
        UnitOfWork.Enqueue((session, ct) =>
            session is null
                ? Collection.DeleteManyAsync(filter, ct)
                : Collection.DeleteManyAsync(session, filter, null, ct));
        return Task.CompletedTask;
    }
}
