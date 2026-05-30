using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Implementação Postgre do read model de logs de auditoria Admin (F7).
/// Projeta direto para <see cref="AdminAuditLogRow"/>, sem expor a entidade.
/// </summary>
public sealed class AdminAuditLogQueries(EasyStockDbContext db) : IAdminAuditLogQueries
{
    public async Task<(IReadOnlyList<AdminAuditLogRow> Items, int Total)> ListarAsync(
        AdminAuditLogFiltro filtro, CancellationToken ct = default)
    {
        var query = BuildQuery(filtro);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.CriadoEm)
            .Skip((filtro.Page - 1) * filtro.PageSize)
            .Take(filtro.PageSize)
            .Select(x => new AdminAuditLogRow(x.Id, x.AdminEmail, x.Acao, x.TenantId, x.Detalhes, x.Ip, x.CriadoEm))
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<IReadOnlyList<AdminAuditLogRow>> ExportarAsync(
        AdminAuditLogFiltro filtro, CancellationToken ct = default)
        => await BuildQuery(filtro)
            .OrderByDescending(x => x.CriadoEm)
            .Select(x => new AdminAuditLogRow(x.Id, x.AdminEmail, x.Acao, x.TenantId, x.Detalhes, x.Ip, x.CriadoEm))
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<AdminAuditLogRow> Items, int Total)> ListarPorAcaoExataAsync(
        string? acao, DateTime? de, DateTime? ate, int page, int pageSize, CancellationToken ct = default)
    {
        var query = db.AdminAuditLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(acao))
            query = query.Where(l => l.Acao == acao);

        if (de.HasValue)
            query = query.Where(l => l.CriadoEm >= de.Value.ToUniversalTime());

        if (ate.HasValue)
            query = query.Where(l => l.CriadoEm < ate.Value.ToUniversalTime().AddDays(1));

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CriadoEm)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new AdminAuditLogRow(l.Id, l.AdminEmail, l.Acao, l.TenantId, l.Detalhes, l.Ip, l.CriadoEm))
            .ToListAsync(ct);

        return (items, total);
    }

    private IQueryable<AdminAuditLog> BuildQuery(AdminAuditLogFiltro filtro)
    {
        var query = db.AdminAuditLogs.AsNoTracking().AsQueryable();

        if (filtro.TenantId.HasValue)
            query = query.Where(x => x.TenantId == filtro.TenantId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Acao))
            query = query.Where(x => x.Acao.Contains(filtro.Acao));

        if (filtro.From.HasValue)
            query = query.Where(x => x.CriadoEm >= filtro.From.Value.ToUniversalTime());

        if (filtro.To.HasValue)
            query = query.Where(x => x.CriadoEm <= filtro.To.Value.ToUniversalTime().AddDays(1));

        return query;
    }
}
