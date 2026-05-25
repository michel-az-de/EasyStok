using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IConfiguracaoLojaRepository
{
    Task<ConfiguracaoLoja?> GetByLojaIdAsync(Guid lojaId);
    Task<ConfiguracaoLoja> GetOrDefaultAsync(Guid lojaId);
    Task AddAsync(ConfiguracaoLoja configuracao);
    Task UpdateAsync(ConfiguracaoLoja configuracao);
}
