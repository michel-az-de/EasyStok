using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Computação das métricas financeiras do Admin (issue 762) — corpo movido do
/// <c>MetricasFinanceirasUseCase</c> para a Infra porque o modo cross-tenant exige RLS bypass.
///
/// <para>
/// Diferente de <see cref="FleetOperationQueries"/>/<see cref="RevenueMetricsQueries"/>
/// (endpoints SuperAdmin-only, bypass incondicional), o endpoint de métricas atende TAMBÉM
/// admin operacional (com <c>empresaId</c> do próprio tenant). Por isso o bypass é
/// CONDICIONAL a <see cref="EasyStockDbContext.IsSuperAdmin"/> (vem do JWT): para o
/// operacional a policy de RLS do Postgres continua sendo a última linha de defesa caso o
/// filtro explícito por empresa algum dia regrida.
/// </para>
/// </summary>
public sealed class MetricasFinanceirasQueries(
    EasyStockDbContext db,
    IFaturaRepository faturaRepo,
    IAssinaturaEmpresaRepository assinaturaRepo) : IMetricasFinanceirasQueries
{
    public async Task<MetricasFinanceirasResult> ComputarAsync(
        int dias, Guid? empresaId, CancellationToken ct = default)
    {
        // Cross-tenant só para SuperAdmin; admin operacional segue sob a RLS do próprio tenant.
        using var _ = db.IsSuperAdmin ? db.UseRowLevelSecurityBypass() : null;

        var fim = DateTime.UtcNow;
        var inicio = fim.AddDays(-dias);

        // Assinaturas — MRR baseia-se em PrecoMensal das Ativas. Filtra por empresa
        // pra evitar vazar MRR/contagens globais a admin operacional.
        var mrr = await assinaturaRepo.SomarPrecoMensalAtivasAsync(empresaId, ct);
        var statusAssinaturas = await assinaturaRepo.ContarPorStatusAsync(empresaId, ct);
        var ativas = statusAssinaturas.GetValueOrDefault(StatusAssinatura.Ativa, 0);
        var suspensas = statusAssinaturas.GetValueOrDefault(StatusAssinatura.Suspensa, 0);
        var canceladas = statusAssinaturas.GetValueOrDefault(StatusAssinatura.Cancelada, 0);

        // Faturas — agregacoes do periodo + estado atual (vencidas).
        var contagensPeriodo = await faturaRepo.ContarPorStatusAsync(inicio, fim, empresaId, ct);
        var totaisPeriodo = await faturaRepo.SomarTotalPorStatusAsync(inicio, fim, empresaId, ct);

        // Vencidas: janela de 365d sobre DataEmissao — vencidas emitidas alem disso
        // nao contam (acomoda corner cases muito antigos sem inflar a query).
        var inicioVencidas = fim.AddDays(-365);
        var contagensVencidas = await faturaRepo.ContarPorStatusAsync(inicioVencidas, fim, empresaId, ct);
        var totaisVencidas = await faturaRepo.SomarTotalPorStatusAsync(inicioVencidas, fim, empresaId, ct);

        var emitidas = contagensPeriodo.GetValueOrDefault(StatusFatura.Emitida, 0)
            + contagensPeriodo.GetValueOrDefault(StatusFatura.Paga, 0)
            + contagensPeriodo.GetValueOrDefault(StatusFatura.ParcialmentePaga, 0)
            + contagensPeriodo.GetValueOrDefault(StatusFatura.Vencida, 0);
        var pagas = contagensPeriodo.GetValueOrDefault(StatusFatura.Paga, 0);
        var vencidas = contagensVencidas.GetValueOrDefault(StatusFatura.Vencida, 0);
        var receita = totaisPeriodo.GetValueOrDefault(StatusFatura.Paga, 0m);
        var valorVencido = totaisVencidas.GetValueOrDefault(StatusFatura.Vencida, 0m);

        var taxa = emitidas > 0 ? Math.Round((decimal)pagas / emitidas * 100m, 1) : 0m;
        var ticketMedio = pagas > 0 ? Math.Round(receita / pagas, 2) : 0m;
        var atrasoMedio = await faturaRepo.MediaDiasAtrasoVencidasAsync(empresaId, ct);
        var topInadimplentes = empresaId.HasValue
            ? Array.Empty<TopInadimplenteResult>() // filtro por 1 empresa elimina top-N
            : await faturaRepo.TopInadimplentesAsync(limit: 5, empresaId: null, ct: ct);

        return new MetricasFinanceirasResult(
            Mrr: mrr,
            Arr: mrr * 12m,
            AssinaturasAtivas: ativas,
            AssinaturasSuspensas: suspensas,
            AssinaturasCanceladas: canceladas,
            FaturasEmitidasPeriodo: emitidas,
            FaturasPagasPeriodo: pagas,
            FaturasVencidas: vencidas,
            TaxaConversao: taxa,
            ReceitaPeriodo: receita,
            ValorVencido: valorVencido,
            TicketMedio: ticketMedio,
            AtrasoMedioDias: Math.Round(atrasoMedio, 1),
            TopInadimplentes: topInadimplentes,
            PeriodoInicio: inicio,
            PeriodoFim: fim
        );
    }
}
