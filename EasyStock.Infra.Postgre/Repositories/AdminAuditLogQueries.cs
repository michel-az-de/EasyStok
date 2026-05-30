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
