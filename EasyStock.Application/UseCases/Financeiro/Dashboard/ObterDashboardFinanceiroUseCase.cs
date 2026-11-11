namespace EasyStock.Application.UseCases.Financeiro.Dashboard;

public sealed record ObterDashboardFinanceiroQuery(Guid EmpresaId, DateTime? ReferenceDateUtc = null);

public class ObterDashboardFinanceiroUseCase(IFluxoCaixaQueries queries, RealizadoDoCaixa caixa)
{
    public async Task<DashboardFinanceiroDto> ExecuteAsync(ObterDashboardFinanceiroQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var referencia = q.ReferenceDateUtc ?? HorarioBrasil.HojeInstanteUtc();
        var kpis = await queries.KpisDashboardAsync(q.EmpresaId, referencia, ct);
        var (ini, fim) = HorarioBrasil.JanelaMesUtc(DateOnly.FromDateTime(referencia));
        var (entrou, saiu) = RealizadoDoCaixa.Somar(await caixa.ListarAsync(q.EmpresaId, ini, fim, ct), ini, fim);
        return kpis with
        {
            TotalRecebidoMes = kpis.TotalRecebidoMes + entrou,
            TotalPagoMes = kpis.TotalPagoMes + saiu,
            TotalRecebidoMesCaixa = entrou,
            TotalPagoMesCaixa = saiu,
        };
    }
}

public sealed record ObterFluxoCaixaQuery(
    Guid EmpresaId,
    PeriodicidadeFluxo Periodicidade,
    DateTime Inicio,
    DateTime Fim,
    Guid? CategoriaId = null,
    Guid? CentroCustoId = null);

public class ObterFluxoCaixaUseCase(IFluxoCaixaQueries queries, RealizadoDoCaixa caixa)
{
    public async Task<IReadOnlyList<FluxoBucketDto>> ExecuteAsync(ObterFluxoCaixaQuery q, CancellationToken ct = default)
    {
        UseCaseGuards.EnsureEmpresaId(q.EmpresaId);
        var buckets = await queries.FluxoBucketsAsync(q.EmpresaId, q.Periodicidade, q.Inicio, q.Fim, q.CategoriaId, q.CentroCustoId, ct);
        // Pedido, venda e movimento do Caixa não têm categoria nem centro de custo: com filtro, só contas.
        if (buckets.Count == 0 || q.CategoriaId.HasValue || q.CentroCustoId.HasValue) return buckets;

        var janelas = buckets.Select(JanelaReal).ToList();
        // O futuro não tem realizado; parar em agora mantém a consulta do tamanho do histórico.
        var fim = janelas[^1].Fim < DateTime.UtcNow ? janelas[^1].Fim : DateTime.UtcNow;
        if (fim <= janelas[0].Ini) return buckets;
        var linhas = await caixa.ListarAsync(q.EmpresaId, janelas[0].Ini, fim, ct);

        return buckets.Select((b, i) =>
        {
            var (entrou, saiu) = RealizadoDoCaixa.Somar(linhas, janelas[i].Ini, janelas[i].Fim);
            return b with
            {
                RealizadoReceber = b.RealizadoReceber + entrou,
                RealizadoPagar = b.RealizadoPagar + saiu,
                RealizadoReceberCaixa = entrou,
                RealizadoPagarCaixa = saiu,
            };
        }).ToList();
    }

    // A linha vai do primeiro ao último dia civil (InicioBucket 00:00Z, FimBucket 23:59:59.9999999Z do dia civil).
    private static (DateTime Ini, DateTime Fim) JanelaReal(FluxoBucketDto b) => (
        HorarioBrasil.InicioRealDoDiaUtc(DateOnly.FromDateTime(b.InicioBucket)),
        HorarioBrasil.InicioRealDoDiaUtc(DateOnly.FromDateTime(b.FimBucket).AddDays(1)));
}
