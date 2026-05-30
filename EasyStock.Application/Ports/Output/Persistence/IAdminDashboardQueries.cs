namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model do dashboard Admin SaaS — métricas agregadas cross-tenant
/// (tenants, receita, tickets, usuários) que viviam direto no
/// AdminDashboardController (F7).
/// </summary>
public interface IAdminDashboardQueries
{
    Task<AdminDashboardData> ObterAsync(DateTime nowUtc, CancellationToken ct = default);
}

public sealed record AdminDashboardData(
    int TotalTenants,
    int TenantsAtivos,
    int TenantsSuspensos,
    int TenantsNovosUltimos30Dias,
    int TicketsAbertos,
    int TicketsCriticos,
    int TicketsEmAtendimento,
    int TicketsComNovaMensagem,
    int TotalUsuariosAtivos,
    int Logins24h,
    decimal ReceitaMensalEstimada,
    IReadOnlyList<DashboardTicketCritico> UltimosTicketsCriticos,
    IReadOnlyList<DashboardTenantRecente> TenantsRecentes);

public sealed record DashboardTicketCritico(
    Guid Id, string Titulo, string EmpresaNome, string Status, string Prioridade, DateTime CriadoEm);

public sealed record DashboardTenantRecente(
    Guid Id, string Nome, string? Documento, DateTime CriadoEm);
