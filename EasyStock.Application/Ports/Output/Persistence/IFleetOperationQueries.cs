namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model do Centro de Comando da Frota (issue 623) — rollup operacional
/// cross-tenant de todas as lojas ATIVAS, espelhando o padrao de
/// <see cref="IAdminDashboardQueries"/>. As linhas vem ordenadas pior-primeiro
/// (HealthScore asc) e capadas em <c>maxLinhas</c>; <see cref="FleetOperationSummary.TotalTenants"/>
/// e o total de tenants ativos (antes do corte) para a UI mostrar "mostrando X de N".
/// </summary>
public interface IFleetOperationQueries
{
    Task<FleetOperationSummary> ObterAsync(DateTime nowUtc, int maxLinhas, CancellationToken ct = default);
}

public sealed record FleetOperationSummary(
    DateTime Generated,
    int TotalTenants,
    FleetTotals Totals,
    IReadOnlyList<FleetTenantRow> Tenants);

public sealed record FleetTotals(
    int TenantsOnline,
    decimal VendasHojeTotal,
    int PedidosTravados,
    int TenantsEmRisco,
    int TicketsSlaViolado,
    int FaturasVencidasCount,
    decimal FaturasVencidasValor,
    decimal MrrAtivo,
    int Suspensos);

public sealed record FleetTenantRow(
    Guid EmpresaId,
    string Nome,
    string? Plano,
    int HealthScore,
    string HealthBand,
    decimal VendasHoje,
    int VendasCount,
    int PedidosAbertos,
    int PedidosTravados,
    int ConferenciaPendente,
    int DevicesAtivos,
    int DevicesTotal,
    int TicketsAbertos,
    int TicketsSlaViolado,
    bool FaturaVencida,
    DateTime? TrialFim,
    IReadOnlyList<string> RiscoFlags);
