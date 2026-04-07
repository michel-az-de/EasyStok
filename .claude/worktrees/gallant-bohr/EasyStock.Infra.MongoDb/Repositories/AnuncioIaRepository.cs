using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class AnuncioIaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IAnuncioIaRepository
{
    private IMongoCollection<AnuncioIa> Collection => Context.GetCollection<AnuncioIa>(MongoCollectionNames.AnunciosIa);

    public async Task<AnuncioIa?> GetByIdAsync(Guid empresaId, Guid id) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Id == id).FirstOrDefaultAsync();

    public async Task<IReadOnlyList<AnuncioIa>> GetByProdutoAsync(Guid empresaId, Guid produtoId)
    {
        return await Collection.Find(x => x.EmpresaId == empresaId && x.ProdutoId == produtoId && x.Salvo)
            .SortByDescending(x => x.CriadoEm)
            .ToListAsync();
    }

    public Task AddAsync(AnuncioIa anuncio)
    {
        EnqueueInsert(Collection, anuncio);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(AnuncioIa anuncio)
    {
        EnqueueReplace(Collection, anuncio.Id, anuncio);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(AnuncioIa anuncio)
    {
        EnqueueDelete(Collection, anuncio.Id);
        return Task.CompletedTask;
    }
}