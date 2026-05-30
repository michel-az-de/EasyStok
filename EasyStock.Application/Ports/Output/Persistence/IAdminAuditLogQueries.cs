namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model dos logs de auditoria Admin SaaS (cross-tenant). Centraliza a
/// projeção paginada + export CSV que antes viviam direto no
/// AdminAuditLogsController (F7 — controllers fora do DbContext concreto).
/// </summary>
public interface IAdminAuditLogQueries
{
    Task<(IReadOnlyList<AdminAuditLogRow> Items, int Total)> ListarAsync(
        AdminAuditLogFiltro filtro, CancellationToken ct = default);

    Task<IReadOnlyList<AdminAuditLogRow>> ExportarAsync(
        AdminAuditLogFiltro filtro, CancellationToken ct = default);
}

public sealed record AdminAuditLogFiltro(
    Guid? TenantId,
    string? Acao,
    DateTime? From,
    DateTime? To,
    int Page,
    int PageSize);

public sealed record AdminAuditLogRow(
    Guid Id,
    string AdminEmail,
    string Acao,
    Guid? TenantId,
    string? Detalhes,
    string? Ip,
    DateTime CriadoEm);
