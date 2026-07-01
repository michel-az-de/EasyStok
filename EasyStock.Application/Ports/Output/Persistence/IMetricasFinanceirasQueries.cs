namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Read-model das métricas financeiras do Admin (issue 762). A computação vive em Infra
/// porque o modo SuperAdmin cross-tenant (<c>empresaId == null</c>) exige abrir o bypass de
/// RLS — API do DbContext que a Application não pode tocar. Sob role sem BYPASSRLS
/// (<c>easystok_user</c> de prod), sem o bypass a policy zera as agregações silenciosamente
/// (mesma classe de bug do MRR do dashboard, issue 754).
/// </summary>
public interface IMetricasFinanceirasQueries
{
    /// <param name="dias">Janela retroativa em dias (já clampeada pelo use-case).</param>
    /// <param name="empresaId">Filtro por empresa — null = cross-tenant (SuperAdmin).</param>
    Task<MetricasFinanceirasResult> ComputarAsync(int dias, Guid? empresaId, CancellationToken ct = default);
}

/// <summary>DTO de metricas financeiras consumido pelo dashboard admin.</summary>
/// <param name="Mrr">Monthly Recurring Revenue (soma de Plano.PrecoMensal de assinaturas Ativas).</param>
/// <param name="Arr">Annual Recurring Revenue = MRR × 12.</param>
/// <param name="AssinaturasAtivas">Quantidade de assinaturas Ativas.</param>
/// <param name="AssinaturasSuspensas">Quantidade de assinaturas Suspensas (proxy de churn em risco).</param>
/// <param name="AssinaturasCanceladas">Quantidade de assinaturas Canceladas (acumulado historico).</param>
/// <param name="FaturasEmitidasPeriodo">Faturas emitidas no periodo (todas excluindo Cancelada).</param>
/// <param name="FaturasPagasPeriodo">Faturas pagas no periodo.</param>
/// <param name="FaturasVencidas">Faturas vencidas (status atual = Vencida — snapshot dos ultimos 365d).</param>
/// <param name="TaxaConversao">% de conversao (pagas / emitidas) no periodo.</param>
/// <param name="ReceitaPeriodo">Soma R$ de faturas Paga no periodo (revenue realizado).</param>
/// <param name="ValorVencido">Soma R$ de faturas Vencida (em aberto agora — receita perdida temporariamente).</param>
/// <param name="TicketMedio">Ticket medio das faturas pagas no periodo (Receita / Pagas).</param>
/// <param name="AtrasoMedioDias">Media de dias de atraso das vencidas em aberto.</param>
/// <param name="TopInadimplentes">Top inadimplentes — empresas com mais faturas vencidas.</param>
/// <param name="PeriodoInicio">Inicio da janela considerada (UTC).</param>
/// <param name="PeriodoFim">Fim da janela considerada (UTC).</param>
public sealed record MetricasFinanceirasResult(
    decimal Mrr,
    decimal Arr,
    int AssinaturasAtivas,
    int AssinaturasSuspensas,
    int AssinaturasCanceladas,
    int FaturasEmitidasPeriodo,
    int FaturasPagasPeriodo,
    int FaturasVencidas,
    decimal TaxaConversao,
    decimal ReceitaPeriodo,
    decimal ValorVencido,
    decimal TicketMedio,
    double AtrasoMedioDias,
    IReadOnlyList<TopInadimplenteResult> TopInadimplentes,
    DateTime PeriodoInicio,
    DateTime PeriodoFim
);
