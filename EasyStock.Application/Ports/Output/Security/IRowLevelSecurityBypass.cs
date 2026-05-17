namespace EasyStock.Application.Ports.Output.Security;

/// <summary>
/// Abstracao para suspender temporariamente o Row Level Security (RLS) do
/// Postgres em operacoes cross-tenant conhecidas: webhooks (que nao tem
/// contexto de tenant — o tenant e descoberto pelo payload), jobs de
/// background (que iteram sobre multiplos tenants), reconciliacoes.
///
/// <para>
/// <b>Uso:</b> sempre dentro de <c>using</c> para garantir restauracao.
/// <code>
/// using var _ = rlsBypass.Begin();
/// // queries aqui veem todos os tenants
/// </code>
/// </para>
///
/// <para>
/// <b>NAO USAR EM REQUEST-PATH:</b> request normal tem JWT com tenant, e o
/// RLS protege contra leak entre tenants. Bypass so deve aparecer em codigo
/// auditavel (jobs + webhooks).
/// </para>
/// </summary>
public interface IRowLevelSecurityBypass
{
    /// <summary>
    /// Habilita o bypass apenas durante o escopo retornado. <c>Dispose</c>
    /// restaura o valor anterior (composavel se aninhado).
    /// </summary>
    IDisposable Begin();
}
