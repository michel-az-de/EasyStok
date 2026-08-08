namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Persistência das feature flags por tenant (ADR-0048): quais módulos cada empresa enxerga.
/// A tabela existia desde a migration de governança, mas ninguém a lia em runtime — o que
/// tornava "módulo por tenant" uma promessa sem implementação.
///
/// <para>
/// ⚠️ <b>Duas proteções estão desligadas para esta entidade.</b> Ela é isenta do global query
/// filter (<c>EasyStockDbContext.SkipTenantFilter</c>) <b>e</b> das políticas de RLS do
/// Postgres (migration <c>AddRowLevelSecurity</c>, lista <c>skip_tables</c>). Nenhuma rede de
/// segurança vai corrigir um filtro esquecido: toda query aqui <b>precisa</b> comparar
/// <c>EmpresaId</c> explicitamente, e o <c>empresaId</c> tem que vir da claim do usuário —
/// nunca do corpo ou da querystring sem passar pela resolução de tenant.
/// </para>
/// </summary>
public interface ITenantFeatureFlagRepository
{
    /// <summary>Flags de UM tenant. Lista vazia quando a empresa não tem nenhuma configurada.</summary>
    Task<IReadOnlyList<TenantFeatureFlagItem>> ListarPorEmpresaAsync(
        Guid empresaId, CancellationToken ct = default);

    /// <summary>
    /// Nomes das features ATIVAS do tenant — o que o produto precisa saber para decidir o que
    /// mostrar. Separado de <see cref="ListarPorEmpresaAsync"/>, que é visão de administração.
    /// </summary>
    Task<IReadOnlyList<string>> ListarAtivasAsync(Guid empresaId, CancellationToken ct = default);

    /// <summary>
    /// Liga ou desliga uma feature do tenant (cria se não existir), registrando quem alterou.
    /// Devolve o estado resultante.
    /// </summary>
    Task<TenantFeatureFlagItem> DefinirAsync(
        Guid empresaId, string feature, bool ativo, string alteradoPor, CancellationToken ct = default);
}

public sealed record TenantFeatureFlagItem(
    string Feature, bool Ativo, DateTime AlteradoEm, string AlteradoPor);
