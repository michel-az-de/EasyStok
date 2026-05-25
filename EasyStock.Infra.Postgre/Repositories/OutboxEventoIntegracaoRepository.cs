using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

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
        return await db.Set<OutboxEventoIntegracao>()
            .IgnoreQueryFilters()
            .Where(e => e.ShardKey == shardKey
                     && e.Status == StatusOutboxIntegracao.Pendente
                     && e.ProximaTentativaEm <= agora)
            .OrderBy(e => e.CriadoEm)
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

    public async Task<TimeSpan> LagDoMaisAntigoPendenteAsync(CancellationToken ct = default)
    {
        var maisAntigo = await db.Set<OutboxEventoIntegracao>()
            .IgnoreQueryFilters()
            .Where(e => e.Status == StatusOutboxIntegracao.Pendente)
            .OrderBy(e => e.CriadoEm)
            .Select(e => (DateTime?)e.CriadoEm)
            .FirstOrDefaultAsync(ct);

        if (maisAntigo is null) return TimeSpan.Zero;

        var lag = DateTime.UtcNow - maisAntigo.Value;
        return lag < TimeSpan.Zero ? TimeSpan.Zero : lag;
    }
}
