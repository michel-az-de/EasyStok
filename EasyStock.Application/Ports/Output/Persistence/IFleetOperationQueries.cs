namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read model da tela Operação do Admin (issue 623) — visão cross-tenant dos clientes
/// combinando a CONTA (assinatura/MRR/tickets/faturas) com a VENDA REAL do ERP (db.Vendas).
/// Espelha o padrão de <see cref="IAdminDashboardQueries"/>. As linhas vêm ordenadas por
/// quem mais precisa de atenção; <see cref="FleetOperationSummary.TotalClientes"/> é o total
/// (antes do corte) para a UI mostrar "mostrando X de N".
/// </summary>
public interface IFleetOperationQueries
{
    Task<FleetOperationSummary> ObterAsync(DateTime nowUtc, int maxLinhas, CancellationToken ct = default);
}

public sealed record FleetOperationSummary(
    DateTime Generated,
    int TotalClientes,
    FleetTotals Totais,
    IReadOnlyList<FleetTenantRow> Clientes);

public sealed record FleetTotals(
    int ClientesAtivos,
    int PrecisamAtencao,
    decimal VendasHojeTotal,
    decimal MrrAtivo,
    int TicketsSlaViolado,
    decimal FaturasVencidasValor,
    int Suspensos);

public sealed record FleetTenantRow(
    Guid EmpresaId,
    string Nome,
    string? Plano,
    decimal Mrr,
    string StatusAssinatura,
    string StatusBand,                  // "ok" | "warn" | "crit"
    IReadOnlyList<string> Motivos,      // chaves de motivo (a UI renderiza em pt-BR com os números)
    decimal VendasHoje,
    int VendasHojeCount,
    int TicketsAbertos,
    int TicketsSlaViolado,
    int FaturasVencidasCount,
    decimal FaturasVencidasValor,
    DateTime? UltimaVendaEm,
    DateTime? TrialFim,
    int Severidade);                    // ordenação: maior = mais precisa de atenção
