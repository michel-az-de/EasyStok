using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class ConfiguracaoAtendimentoRepository(EasyStockDbContext dbContext) : IConfiguracaoAtendimentoRepository
{
    // #2096: jobs entre empresas (lembretes, caixa esquecido) rodam sem tenant; o filtro global zerava a leitura e o
    // interruptor da tela Avisos caía no padrão. A empresa vai explícita no WHERE e a RLS do banco continua valendo.
    public Task<ConfiguracaoAtendimento?> GetByEmpresaIdAsync(Guid empresaId) =>
        dbContext.ConfiguracoesAtendimento
            .IgnoreQueryFilters()
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
