using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.Security;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// As leituras do Worker (lote de pendentes e reclamação de presos) atravessam empresas por natureza e rodam
/// sob bypass de RLS (#3108): a tabela tem a policy <c>tenant_isolation</c> e o Worker não tem tenant. O
/// processamento de cada evento segue com o tenant do evento, fixado pelo dispatcher.
/// </summary>
public sealed class OutboxEventoIntegracaoRepository(EasyStockDbContext db) : IOutboxEventoIntegracaoRepository
{
    public Task AddAsync(OutboxEventoIntegracao evento, CancellationToken ct = default)
    {
        db.Set<OutboxEventoIntegracao>().Add(evento);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(OutboxEventoIntegracao evento, CancellationToken ct = default)
    {
        db.Set<OutboxEventoIntegracao>().Update(evento);
        return Task.CompletedTask;
    }

    public Task<OutboxEventoIntegracao?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Set<OutboxEventoIntegracao>().FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task<IReadOnlyList<OutboxEventoIntegracao>> ProximosPendentesAsync(
        int shardKey,
        int max,
        CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);
        return await db.Set<OutboxEventoIntegracao>()
            .IgnoreQueryFilters()
            .Where(e => e.ShardKey == shardKey
                     && e.Status == StatusOutboxIntegracao.Pendente
                     && e.ProximaTentativaEm <= agora)
            .OrderBy(e => e.CriadoEm)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxEventoIntegracao>> ProximosPendentesDeTodosOsShardsAsync(
        int max,
        CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);
        return await db.Set<OutboxEventoIntegracao>()
            .IgnoreQueryFilters()
            .Where(e => e.Status == StatusOutboxIntegracao.Pendente
                     && e.ProximaTentativaEm <= agora)
            // FIFO global: o shard é um hash, não uma partição, então ordenar por ele embaralha a sequência.
            .OrderBy(e => e.CriadoEm)
            .ThenBy(e => e.Id)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxEventoIntegracao>> ListarPorEmpresaAsync(
        Guid empresaId,
        StatusOutboxIntegracao? status = null,
        string? tipoEvento = null,
        int max = 100,
        CancellationToken ct = default)
    {
        var query = db.Set<OutboxEventoIntegracao>()
            .Where(e => e.EmpresaId == empresaId);

        if (status.HasValue)
            query = query.Where(e => e.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(tipoEvento))
            query = query.Where(e => e.TipoEvento == tipoEvento);

        return await query
            .OrderByDescending(e => e.CriadoEm)
            .Take(max)
            .ToListAsync(ct);
    }

    public async Task<int> ReclamarEmEnvioExpiradosAsync(int max, CancellationToken ct = default)
    {
        var agora = DateTime.UtcNow;
        await using var bypass = await db.UseRowLevelSecurityBypassAsync(ct);
        var presos = await db.Set<OutboxEventoIntegracao>()
            .IgnoreQueryFilters()
            .Where(e => e.Status == StatusOutboxIntegracao.EmEnvio
                     && e.ProximaTentativaEm < agora)
            .OrderBy(e => e.ProximaTentativaEm)
            .Take(max)
            .ToListAsync(ct);

        if (presos.Count == 0) return 0;

        foreach (var evt in presos)
            evt.ReclamarEmEnvioExpirado();

        await db.SaveChangesAsync(ct);
        return presos.Count;
    }

}
