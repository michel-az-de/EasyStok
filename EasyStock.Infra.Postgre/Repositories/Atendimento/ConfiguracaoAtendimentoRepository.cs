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

    // #2096: os jobs entre empresas (lembretes, caixa esquecido) rodam sem tenant, e o filtro global zerava a leitura:
    // o aviso desligado na tela continuava saindo. Só o documento dos avisos sai daqui, pela empresa que o produtor do
    // servidor informa; GetByEmpresaIdAsync segue com o filtro do tenant.
    public async Task<AvisosOperacao> ObterAvisosAsync(Guid empresaId, CancellationToken ct = default) =>
        await dbContext.ConfiguracoesAtendimento
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.EmpresaId == empresaId)
            .Select(x => x.Avisos)
            .FirstOrDefaultAsync(ct) ?? AvisosOperacao.Padrao;

    public Task AddAsync(ConfiguracaoAtendimento configuracao) =>
        dbContext.ConfiguracoesAtendimento.AddAsync(configuracao).AsTask();

    public Task UpdateAsync(ConfiguracaoAtendimento configuracao)
    {
        dbContext.ConfiguracoesAtendimento.Update(configuracao);
        return Task.CompletedTask;
    }
}
