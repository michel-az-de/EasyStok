using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class ConfiguracaoCanalRepository(EasyStockDbContext db) : IConfiguracaoCanalRepository
{
    public Task<ConfiguracaoCanal?> GetAsync(
        CanalNotificacao canal, Guid? empresaId, CancellationToken ct = default) =>
        Escopo(empresaId)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Canal == canal && c.EmpresaId == empresaId, ct);

    public async Task<IReadOnlyList<ConfiguracaoCanal>> ListarAsync(
        Guid? empresaId, CancellationToken ct = default) =>
        await Escopo(empresaId)
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .ToListAsync(ct);

    /// <summary>
    /// Configuração global (<c>EmpresaId</c> nulo, N1): o filtro do EF a esconde de quem está no escopo de uma empresa
    /// e, sem canal visível, o evento fecha sem outbox. A policy <c>catalogo_global_leitura</c> a expõe ao tenant (só
    /// SELECT) e o <c>WHERE</c> das consultas já delimita a linha global; a da empresa segue sob o filtro do EF.
    /// </summary>
    private IQueryable<ConfiguracaoCanal> Escopo(Guid? empresaId) =>
        empresaId is null ? db.NotifConfiguracoesCanal.IgnoreQueryFilters() : db.NotifConfiguracoesCanal;

    public async Task AddAsync(ConfiguracaoCanal config, CancellationToken ct = default) =>
        await db.NotifConfiguracoesCanal.AddAsync(config, ct);

    public Task UpdateAsync(ConfiguracaoCanal config, CancellationToken ct = default)
    {
        db.NotifConfiguracoesCanal.Update(config);
        return Task.CompletedTask;
    }
}
