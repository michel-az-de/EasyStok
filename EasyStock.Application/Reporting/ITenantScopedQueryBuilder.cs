namespace EasyStock.Application.Reporting;

/// <summary>
/// Defesa em profundidade contra vazamento cross-tenant em handlers de relatório.
/// Todo handler usa este builder em vez de acessar <c>db.Set&lt;T&gt;()</c> diretamente.
/// Aplica <c>IgnoreQueryFilters()</c> + <c>WHERE EmpresaId = @scope</c> explicitamente,
/// garantindo isolamento mesmo que o <see cref="IReportExecutionScope"/> falhe silenciosamente.
/// O contexto de banco de dados é injetado via DI na implementação (Infrastructure).
/// </summary>
public interface ITenantScopedQueryBuilder
{
    /// <summary>
    /// Retorna uma query com <c>WHERE EmpresaId = scope.EmpresaId</c> explícito.
    /// Lança <see cref="InvalidOperationException"/> se o escopo não foi inicializado.
    /// </summary>
    IQueryable<T> Query<T>() where T : class;

    /// <summary>
    /// Para uso em relatórios AdminSaaS — aplica apenas <c>IgnoreQueryFilters()</c>
    /// sem restringir por EmpresaId.
    /// Lança se contexto não for AdminSaaS.
    /// </summary>
    IQueryable<T> AdminQuery<T>() where T : class;
}
