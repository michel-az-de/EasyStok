using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.MongoDb.Data;
using MongoDB.Driver;

namespace EasyStock.Infra.MongoDb.Repositories;

public sealed class ConfiguracaoLojaRepository(MongoEasyStockContext context, MongoUnitOfWork unitOfWork)
    : MongoRepositoryBase(context, unitOfWork), IConfiguracaoLojaRepository
{
    private IMongoCollection<ConfiguracaoLoja> Collection => Context.GetCollection<ConfiguracaoLoja>(MongoCollectionNames.ConfiguracoesLoja);

    public async Task<ConfiguracaoLoja?> GetByLojaIdAsync(Guid lojaId) =>
        await Collection.Find(x => x.LojaId == lojaId).FirstOrDefaultAsync();

    public async Task<ConfiguracaoLoja> GetOrDefaultAsync(Guid lojaId) =>
        await GetByLojaIdAsync(lojaId) ?? ConfiguracaoLoja.CriarPadrao(lojaId);

    public Task AddAsync(ConfiguracaoLoja configuracao)
    {
        EnqueueInsert(Collection, configuracao);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ConfiguracaoLoja configuracao)
    {
        EnqueueReplace(Collection, configuracao.Id, configuracao);
        return Task.CompletedTask;
    }
}
