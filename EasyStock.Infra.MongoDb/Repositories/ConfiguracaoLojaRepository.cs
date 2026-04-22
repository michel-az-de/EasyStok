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
        // NOTA multi-tenant: ConfiguracaoLoja é escopada por LojaId (não EmpresaId).
        // O use-case upstream deve verificar que lojaId pertence à empresa do chamador
        // antes de invocar este método. Ver EnqueueReplaceScoped para entidades com EmpresaId.
        EnqueueReplace(Collection, configuracao.Id, configuracao);
        return Task.CompletedTask;
    }
}
