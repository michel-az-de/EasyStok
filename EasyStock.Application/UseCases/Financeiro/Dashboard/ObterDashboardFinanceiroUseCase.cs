namespace EasyStock.Application.UseCases.Financeiro.Dashboard;

public sealed record ObterDashboardFinanceiroQuery(Guid EmpresaId, DateTime? ReferenceDateUtc = null);

public class ObterDashboardFinanceiroUseCase(IFluxoCaixaQueries queries)
{
    public Task<DashboardFinanceiroDto> ExecuteAsync(ObterDashboardFinanceiroQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        return queries.KpisDashboardAsync(q.EmpresaId, q.ReferenceDateUtc ?? DateTime.UtcNow, ct);
    }
}

public sealed record ObterFluxoCaixaQuery(
    Guid EmpresaId,
    PeriodicidadeFluxo Periodicidade,
    DateTime Inicio,
    DateTime Fim,
    Guid? CategoriaId = null,
    Guid? CentroCustoId = null);

public class ObterFluxoCaixaUseCase(IFluxoCaixaQueries queries)
{
    public Task<IReadOnlyList<FluxoBucketDto>> ExecuteAsync(ObterFluxoCaixaQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        return queries.FluxoBucketsAsync(q.EmpresaId, q.Periodicidade, q.Inicio, q.Fim, q.CategoriaId, q.CentroCustoId, ct);
    }
}
