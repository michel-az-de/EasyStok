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
        var categorias = await Collection.Find(x => x.EmpresaId == empresaId).SortBy(x => x.Nome).ToListAsync();
        var lookup = categorias.ToLookup(x => x.CategoriaPaiId);

        foreach (var categoria in categorias)
            categoria.SubCategorias = lookup[categoria.Id].ToList();

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
        EnqueueReplace(Collection, categoria.Id, categoria);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id)
    {
        EnqueueDelete(Collection, id);
        return Task.CompletedTask;
    }
}

public sealed class EmpresaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IEmpresaRepository
{
    private IMongoCollection<Empresa> Collection => Context.GetCollection<Empresa>(MongoCollectionNames.Empresas);

    public Task<Empresa?> GetByIdAsync(Guid id) =>
        Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

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

    public Task<Produto?> GetByIdAsync(Guid id) =>
        Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public Task<Produto?> GetByIdAsync(Guid empresaId, Guid id) =>
        Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<Produto>> SearchAsync(Guid empresaId, string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return [];

        var pattern = BuildContainsPattern(termo);
        var regex = new MongoDB.Bson.BsonRegularExpression(pattern, "i");
        var filter = Builders<Produto>.Filter.And(
            Builders<Produto>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<Produto>.Filter.Or(
                Builders<Produto>.Filter.Regex(x => x.Nome, regex),
                Builders<Produto>.Filter.Regex(x => x.Marca, regex),
                Builders<Produto>.Filter.Regex(x => x.DescricaoBase, regex),
                Builders<Produto>.Filter.Regex("SkuBase", regex),
                Builders<Produto>.Filter.Regex(x => x.CodigoBarras, regex)));

        return await Collection.Find(filter).Limit(50).ToListAsync();
    }

    public async Task<(IEnumerable<Produto> Produtos, int TotalCount)> GetProdutosPaginadosAsync(Guid empresaId, int page = 1, int pageSize = 20)
    {
        var filter = Builders<Produto>.Filter.Eq(x => x.EmpresaId, empresaId);
        var total = (int)await Collection.CountDocumentsAsync(filter);
        var items = await Collection.Find(filter)
            .SortBy(x => x.Nome)
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        return (items, total);
    }

    public Task InsertAsync(Produto produto)
    {
        EnqueueInsert(Collection, produto);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Produto produto)
    {
        EnqueueReplace(Collection, produto.Id, produto);
        return Task.CompletedTask;
    }
}

public sealed class ProdutoVariacaoRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoVariacaoRepository
{
    private IMongoCollection<ProdutoVariacao> Collection => Context.GetCollection<ProdutoVariacao>(MongoCollectionNames.ProdutosVariacao);

    public Task<ProdutoVariacao?> GetByIdAsync(Guid id) =>
        Collection.Find(x => x.Id == id).FirstOrDefaultAsync();

    public async Task<IEnumerable<ProdutoVariacao>> SearchAsync(Guid empresaId, string termo)
    {
        if (string.IsNullOrWhiteSpace(termo))
            return [];

        var pattern = BuildContainsPattern(termo);
        var regex = new MongoDB.Bson.BsonRegularExpression(pattern, "i");
        var filter = Builders<ProdutoVariacao>.Filter.And(
            Builders<ProdutoVariacao>.Filter.Eq(x => x.EmpresaId, empresaId),
            Builders<ProdutoVariacao>.Filter.Or(
                Builders<ProdutoVariacao>.Filter.Regex(x => x.Nome, regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.Cor, regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.Tamanho, regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.DescricaoComercial, regex),
                Builders<ProdutoVariacao>.Filter.Regex("Sku", regex),
                Builders<ProdutoVariacao>.Filter.Regex(x => x.CodigoBarras, regex)));

        return await Collection.Find(filter).Limit(50).ToListAsync();
    }

    public Task InsertAsync(ProdutoVariacao variacao)
    {
        EnqueueInsert(Collection, variacao);
        return Task.CompletedTask;
    }
}

public sealed class ProdutoCaracteristicaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoCaracteristicaRepository
{
    private IMongoCollection<ProdutoCaracteristica> Collection => Context.GetCollection<ProdutoCaracteristica>(MongoCollectionNames.ProdutosCaracteristica);

    public Task InsertAsync(ProdutoCaracteristica caracteristica)
    {
        EnqueueInsert(Collection, caracteristica);
        return Task.CompletedTask;
    }
}

public sealed class ProdutoEmbalagemRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IProdutoEmbalagemRepository
{
    private IMongoCollection<ProdutoEmbalagem> Collection => Context.GetCollection<ProdutoEmbalagem>(MongoCollectionNames.ProdutosEmbalagem);

    public Task InsertAsync(ProdutoEmbalagem embalagem)
    {
        EnqueueInsert(Collection, embalagem);
        return Task.CompletedTask;
    }
}
