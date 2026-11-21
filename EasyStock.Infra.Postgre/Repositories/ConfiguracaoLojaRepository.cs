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
        // Sem loja indicada vale a loja unica ATIVA: a desativada (historico) nao conta.
        query = lojaId.HasValue ? query.Where(c => c.LojaId == lojaId.Value) : query.Where(c => c.Loja!.Ativa);

        var achadas = await query.Take(2).ToListAsync(ct);
        return achadas.Count == 1 ? achadas[0] : null;
    }

    public Task AddAsync(ConfiguracaoLoja configuracao) =>
        dbContext.ConfiguracoesLoja.AddAsync(configuracao).AsTask();

    public Task UpdateAsync(ConfiguracaoLoja configuracao)
    {
        var entrada = dbContext.ConfiguracoesLoja.Update(configuracao);
        if (entrada.State != EntityState.Modified) return Task.CompletedTask;
        // #2199: a configuração chega destacada (lida sem tracking), então o Update marcaria todas as colunas. A regra de
        // impressão tem gravação própria, condicional na versão; sem isto, salvar alertas apagaria a regra gravada no meio.
        entrada.Property(x => x.ImpressaoJson).IsModified = false;
        entrada.Property(x => x.ImpressaoVersao).IsModified = false;
        entrada.Property(x => x.ImpressaoAlteradaEm).IsModified = false;
        return Task.CompletedTask;
    }
}
