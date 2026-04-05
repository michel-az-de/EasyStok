using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class ConfiguracaoLojaRepository(EasyStockDbContext dbContext) : IConfiguracaoLojaRepository
{
    public Task<ConfiguracaoLoja?> GetByLojaIdAsync(Guid lojaId) =>
        dbContext.ConfiguracoesLoja
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LojaId == lojaId);

    public async Task<ConfiguracaoLoja> GetOrDefaultAsync(Guid lojaId) =>
        await GetByLojaIdAsync(lojaId) ?? ConfiguracaoLoja.CriarPadrao(lojaId);

    public Task AddAsync(ConfiguracaoLoja configuracao) =>
        dbContext.ConfiguracoesLoja.AddAsync(configuracao).AsTask();

    public Task UpdateAsync(ConfiguracaoLoja configuracao)
    {
        dbContext.ConfiguracoesLoja.Update(configuracao);
        return Task.CompletedTask;
    }
}
