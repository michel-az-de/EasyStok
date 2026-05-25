namespace EasyStock.Application.Ports.Output.Persistence;

/// <summary>
/// Queries read-only agregadas pra dashboard financeiro e fluxo de caixa.
/// EmpresaId e SEMPRE obrigatorio — anti-vazamento multi-tenant em queries
/// agregadas (ver do-not-do.md item 12).
/// </summary>
public interface IFluxoCaixaQueries
{
    Task<DashboardFinanceiroDto> KpisDashboardAsync(Guid empresaId, DateTime referenceDateUtc, CancellationToken ct = default);

    Task<IReadOnlyList<FluxoBucketDto>> FluxoBucketsAsync(
        Guid empresaId,
        PeriodicidadeFluxo periodicidade,
        DateTime inicio,
        DateTime fim,
        Guid? categoriaId = null,
        Guid? centroCustoId = null,
        CancellationToken ct = default);
}

public sealed record DashboardFinanceiroDto(
    decimal TotalAVencer30dPagar,
    decimal TotalAVencer30dReceber,
    decimal TotalVencidoPagar,
    decimal TotalVencidoReceber,
    decimal TotalPagoMes,
    decimal TotalRecebidoMes,
    int QtdContasPagarAbertas,
    int QtdContasReceberAbertas,
    int QtdParcelasVencidasHoje);

public sealed record FluxoBucketDto(
    DateTime InicioBucket,
    DateTime FimBucket,
    string Rotulo,
    decimal PrevistoPagar,
    decimal PrevistoReceber,
    decimal RealizadoPagar,
    decimal RealizadoReceber);

public enum PeriodicidadeFluxo
{
    Diario = 0,
    Semanal = 1,
    Mensal = 2
}
