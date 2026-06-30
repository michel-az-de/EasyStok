namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Fonte unica de verdade das metricas de receita do Admin (ADR-0037 adendo, issue #754).
/// Calculo AO VIVO do mes corrente. A agregacao cross-tenant roda na camada Infra sob
/// <c>db.UseRowLevelSecurityBypass()</c> (mesmo mecanismo de <c>FleetOperationQueries</c>),
/// porque a RLS (role sem BYPASSRLS) restringiria a leitura ao tenant da sessao.
/// </summary>
public interface IRevenueMetricsQueries
{
    /// <summary>
    /// Snapshot ao vivo do mes corrente (parcial, 1o dia BRT ate agora). <paramref name="empresaId"/>
    /// nulo = cross-tenant (SuperAdmin); informado = restringe ao tenant.
    /// </summary>
    Task<RevenueSnapshot> ObterAsync(Guid? empresaId = null, CancellationToken ct = default);
}

/// <summary>
/// Tres grandezas distintas de receita, nunca um numero unico ambiguo (ADR-0037 adendo):
/// <list type="bullet">
///   <item><c>MrrContratado</c>: SUM(Plano.PrecoMensal) das assinaturas Ativa (o contratado/estimado).</item>
///   <item><c>MrrFaturado</c>: SUM(Fatura.Total) de Origem=Assinatura emitidas no mes (por DataEmissao, exclui Cancelada).</item>
///   <item><c>RecebidoMes</c>: SUM(Fatura.Total) cujo pagamento total ocorreu no mes (por DataPagamentoTotal) — caixa real.</item>
/// </list>
/// </summary>
public sealed record RevenueSnapshot(
    decimal MrrContratado,
    decimal MrrFaturado,
    decimal RecebidoMes,
    DateTime MesInicioUtc,
    DateTime MesFimUtc);
