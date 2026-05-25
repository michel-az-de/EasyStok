using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using EasyStock.Application.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Reporting;

/// <summary>
/// Defesa em profundidade (ADR-R07): toda query de relatório passa por aqui.
/// Aplica WHERE EmpresaId = @scope explicitamente, mesmo com Query Filter ativo.
/// Cache de expressões compiladas por tipo para evitar overhead de reflection repetitivo.
/// Recebe EasyStockDbContext via DI (não via parâmetro de método) para manter a
/// interface ITenantScopedQueryBuilder livre de dependências de infraestrutura.
/// </summary>
public sealed class TenantScopedQueryBuilder(
    EasyStockDbContext db,
    IReportExecutionScope scope) : ITenantScopedQueryBuilder
{
    // Cache thread-safe de expressões: TEntity → Expression<Func<TEntity, bool>> (stored as object)
    private static readonly ConcurrentDictionary<Type, object> _filterCache = new();

    /// <summary>
    /// Query com isolamento por tenant (contexto Tenant).
    /// Aplica WHERE EmpresaId = @scope.EmpresaId explicitamente.
    /// </summary>
    public IQueryable<T> Query<T>() where T : class
    {
        EnsureSet();
        var empresaId = scope.EmpresaId;

        var filter = (Expression<Func<T, bool>>)_filterCache.GetOrAdd(
            typeof(T),
            static _ => BuildEmpresaIdFilter<T>());

        // Substitui o parâmetro na expressão com o ID real da run
        var replaced = ReplaceParameter<T>(filter, empresaId);

        return db.Set<T>()
            .IgnoreQueryFilters()
            .Where(replaced)
            .AsNoTracking();
    }

    /// <summary>
    /// Query para Admin SaaS — cross-tenant explícito.
    /// Sem WHERE EmpresaId (intencional e auditável — apenas Admin chama isto).
    /// </summary>
    public IQueryable<T> AdminQuery<T>() where T : class
    {
        EnsureSet();

        if (scope.Contexto != Domain.Reporting.ReportContexto.AdminSaaS)
            throw new InvalidOperationException(
                "AdminQuery só pode ser chamado em contexto AdminSaaS.");

        return db.Set<T>()
            .IgnoreQueryFilters()
            .AsNoTracking();
    }

    // ── Privado ───────────────────────────────────────────────────────────────

    private void EnsureSet()
    {
        if (!scope.IsSet)
            throw new InvalidOperationException(
                "ITenantScopedQueryBuilder: contexto de execução não inicializado. " +
                "Chame IReportExecutionScope.Begin() antes de usar o QueryBuilder.");
    }

    /// <summary>
    /// Constrói expressão lambda x => x.EmpresaId == empresaId.
    /// Se a entidade não tem EmpresaId, lança InternalError (bug no handler).
    /// </summary>
    private static Expression<Func<T, bool>> BuildEmpresaIdFilter<T>()
    {
        var type = typeof(T);
        var prop = type.GetProperty("EmpresaId", BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"Entidade '{type.Name}' não tem propriedade 'EmpresaId'. " +
                $"Use AdminQuery<T>() para entidades sem EmpresaId.");

        // param => param.EmpresaId == __placeholder__
        var param = Expression.Parameter(type, "e");
        var member = Expression.Property(param, prop);
        var placeholder = Expression.Constant(Guid.Empty, prop.PropertyType);
        var body = Expression.Equal(member, placeholder);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// Substitui o Guid.Empty placeholder pelo valor real do scope.
    /// </summary>
    private static Expression<Func<T, bool>> ReplaceParameter<T>(
        Expression<Func<T, bool>> template,
        Guid? empresaId)
    {
        var visitor = new ConstantReplaceVisitor(Guid.Empty, empresaId);
        var newBody = visitor.Visit(template.Body)!;
        return Expression.Lambda<Func<T, bool>>(newBody, template.Parameters);
    }

    private sealed class ConstantReplaceVisitor(Guid oldValue, Guid? newValue) : ExpressionVisitor
    {
        protected override Expression VisitConstant(ConstantExpression node)
        {
            if (node.Value is Guid g && g == oldValue)
                return Expression.Constant(newValue, typeof(Guid?));
            return base.VisitConstant(node);
        }
    }
}
