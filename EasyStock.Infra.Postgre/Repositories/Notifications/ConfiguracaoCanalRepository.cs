using EasyStock.Application.Ports.Output.Notifications;
using EasyStock.Domain.Entities.Notifications;
using EasyStock.Domain.Enums.Notifications;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Notifications;

public sealed class ConfiguracaoCanalRepository(EasyStockDbContext db) : IConfiguracaoCanalRepository
{
    public Task<ConfiguracaoCanal?> GetAsync(
        CanalNotificacao canal, Guid? empresaId, CancellationToken ct = default) =>
        db.NotifConfiguracoesCanal
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Canal == canal && c.EmpresaId == empresaId, ct);

    public async Task<IReadOnlyList<ConfiguracaoCanal>> ListarAsync(
        Guid? empresaId, CancellationToken ct = default) =>
        await db.NotifConfiguracoesCanal
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .ToListAsync(ct);

    public async Task AddAsync(ConfiguracaoCanal config, CancellationToken ct = default) =>
        await db.NotifConfiguracoesCanal.AddAsync(config, ct);

    public Task UpdateAsync(ConfiguracaoCanal config, CancellationToken ct = default)
    {
        db.NotifConfiguracoesCanal.Update(config);
        return Task.CompletedTask;
    }
}
