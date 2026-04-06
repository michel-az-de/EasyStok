using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class UsoIaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IUsoIaRepository
{
    private IMongoCollection<UsoIa> Collection => Context.GetCollection<UsoIa>(MongoCollectionNames.UsoIa);

    public async Task<UsoIa?> GetAsync(Guid empresaId, int ano, int mes) =>
        await Collection.Find(x => x.EmpresaId == empresaId && x.Ano == ano && x.Mes == mes).FirstOrDefaultAsync();

    public Task AddAsync(UsoIa uso)
    {
        EnqueueInsert(Collection, uso);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(UsoIa uso)
    {
        EnqueueReplace(Collection, uso.Id, uso);
        return Task.CompletedTask;
    }
}