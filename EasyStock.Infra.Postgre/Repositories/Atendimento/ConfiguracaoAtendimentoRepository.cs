using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class ConfiguracaoAtendimentoRepository(EasyStockDbContext dbContext) : IConfiguracaoAtendimentoRepository
{
    public Task<ConfiguracaoAtendimento?> GetByEmpresaIdAsync(Guid empresaId) =>
        dbContext.ConfiguracoesAtendimento
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.EmpresaId == empresaId);

    public async Task<ConfiguracaoAtendimento> GetOrDefaultAsync(Guid empresaId) =>
        await GetByEmpresaIdAsync(empresaId) ?? ConfiguracaoAtendimento.CriarPadrao(empresaId);

    public Task AddAsync(ConfiguracaoAtendimento configuracao) =>
        dbContext.ConfiguracoesAtendimento.AddAsync(configuracao).AsTask();

    public Task UpdateAsync(ConfiguracaoAtendimento configuracao)
    {
        dbContext.ConfiguracoesAtendimento.Update(configuracao);
        return Task.CompletedTask;
    }
}
