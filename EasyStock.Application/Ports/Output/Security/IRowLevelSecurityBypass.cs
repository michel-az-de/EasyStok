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
/// <b>NAO USAR EM REQUEST-PATH AUTENTICADO:</b> request normal tem JWT com
/// tenant, e o RLS protege contra leak entre tenants. Bypass so deve aparecer
/// em codigo auditavel.
/// </para>
///
/// <para>
/// <b>Excecao medida (issue #1024):</b> o registro de empresa
/// (<c>RegistrarEmpresaUseCase</c>) e request-path, mas ANONIMO e cross-tenant
/// por definicao — ele CRIA o tenant, entao nao existe <c>app.empresa_id</c>
/// para o interceptor emitir. Sem bypass, a policy <c>tenant_isolation</c>
/// recusa os INSERTs com <c>42501</c> e o signup responde 500. O criterio que
/// separa este caso de um mau uso nao e "tem JWT?", e sim: <b>a operacao cria
/// ou atravessa tenants por natureza?</b> Se a resposta for nao e a query
/// voltou vazia, o defeito esta no contexto de tenant — bypass ali vira
/// vazamento entre clientes.
/// </para>
///
/// <para>
/// Quem pode injetar este port e travado por <c>RlsBypassAllowlistTests</c>
/// nos arch tests: consumidor novo exige editar a allowlist, o que faz a
/// decisao aparecer no diff da PR em vez de escorregar num construtor.
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
