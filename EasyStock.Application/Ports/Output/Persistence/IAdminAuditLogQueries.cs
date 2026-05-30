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

    /// <summary>
    /// Variante do back-office /audit-admin: filtro por ação EXATA (igualdade) e
    /// intervalo [de, ate) — distinto do ListarAsync (que usa Contains + tenant).
    /// </summary>
    Task<(IReadOnlyList<AdminAuditLogRow> Items, int Total)> ListarPorAcaoExataAsync(
        string? acao, DateTime? de, DateTime? ate, int page, int pageSize, CancellationToken ct = default);
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
