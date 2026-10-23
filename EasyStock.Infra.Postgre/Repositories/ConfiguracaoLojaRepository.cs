using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class ConfiguracaoLojaRepository(EasyStockDbContext dbContext) : IConfiguracaoLojaRepository
{
    public Task<ConfiguracaoLoja?> GetByLojaIdAsync(Guid lojaId) =>
        dbContext.ConfiguracoesLoja
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LojaId == lojaId);

    public async Task<ConfiguracaoLoja> GetOrDefaultAsync(Guid lojaId) =>
        await GetByLojaIdAsync(lojaId) ?? ConfiguracaoLoja.CriarPadrao(lojaId);

    public Task<ConfiguracaoLoja?> GetDaLojaAsync(Guid empresaId, Guid? lojaId, CancellationToken ct = default) =>
        DaLojaAsync(dbContext.ConfiguracoesLoja, empresaId, lojaId, ct);

    /// <summary>
    /// A consulta de <see cref="GetDaLojaAsync"/> sobre qualquer fonte de configurações, para o sync do
    /// aparelho (que usa o contexto direto) não repetir a regra da loja única.
    /// </summary>
    public static async Task<ConfiguracaoLoja?> DaLojaAsync(
        IQueryable<ConfiguracaoLoja> configuracoes, Guid empresaId, Guid? lojaId, CancellationToken ct = default)
    {
        var query = configuracoes.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Loja != null && c.Loja.EmpresaId == empresaId);
        if (lojaId.HasValue) query = query.Where(c => c.LojaId == lojaId.Value);

        var achadas = await query.Take(2).ToListAsync(ct);
        return achadas.Count == 1 ? achadas[0] : null;
    }

    public Task AddAsync(ConfiguracaoLoja configuracao) =>
        dbContext.ConfiguracoesLoja.AddAsync(configuracao).AsTask();

    public Task UpdateAsync(ConfiguracaoLoja configuracao)
    {
        dbContext.ConfiguracoesLoja.Update(configuracao);
        return Task.CompletedTask;
    }
}
