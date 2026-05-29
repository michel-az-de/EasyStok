namespace EasyStock.Application.UseCases.Faturas.MetricasFinanceiras;

/// <summary>Comando do dashboard financeiro.</summary>
/// <param name="DiasRetroativo">Janela retroativa em dias para o calculo de "no periodo" (default 30, clampeado [1,365]).</param>
/// <param name="EmpresaId">Filtro opcional por empresa — admin operacional ja injeta sua empresa via controller.</param>
/// <param name="ForcarRefresh">F13 — quando true, ignora cache e recalcula. Default false (TTL 5min).</param>
public sealed record MetricasFinanceirasCommand(
    int DiasRetroativo = 30,
    Guid? EmpresaId = null,
    bool ForcarRefresh = false
);

/// <summary>DTO de metricas financeiras consumido pelo dashboard admin.</summary>
/// <param name="Mrr">Monthly Recurring Revenue (soma de Plano.PrecoMensal de assinaturas Ativas).</param>
/// <param name="Arr">Annual Recurring Revenue = MRR × 12.</param>
/// <param name="AssinaturasAtivas">Quantidade de assinaturas Ativas.</param>
/// <param name="AssinaturasSuspensas">Quantidade de assinaturas Suspensas (proxy de churn em risco).</param>
/// <param name="AssinaturasCanceladas">Quantidade de assinaturas Canceladas (acumulado historico).</param>
/// <param name="FaturasEmitidasPeriodo">Faturas emitidas no periodo (todas excluindo Cancelada).</param>
/// <param name="FaturasPagasPeriodo">Faturas pagas no periodo.</param>
/// <param name="FaturasVencidas">Faturas vencidas (status atual = Vencida — snapshot dos ultimos 365d).</param>
/// <param name="TaxaConversao">% de conversao (pagas / emitidas) no periodo.</param>
/// <param name="ReceitaPeriodo">Soma R$ de faturas Paga no periodo (revenue realizado).</param>
/// <param name="ValorVencido">Soma R$ de faturas Vencida (em aberto agora — receita perdida temporariamente).</param>
/// <param name="TicketMedio">Ticket medio das faturas pagas no periodo (Receita / Pagas).</param>
/// <param name="AtrasoMedioDias">Media de dias de atraso das vencidas em aberto.</param>
/// <param name="TopInadimplentes">Top inadimplentes — empresas com mais faturas vencidas.</param>
/// <param name="PeriodoInicio">Inicio da janela considerada (UTC).</param>
/// <param name="PeriodoFim">Fim da janela considerada (UTC).</param>
public sealed record MetricasFinanceirasResult(
    decimal Mrr,
    decimal Arr,
    int AssinaturasAtivas,
    int AssinaturasSuspensas,
    int AssinaturasCanceladas,
    int FaturasEmitidasPeriodo,
    int FaturasPagasPeriodo,
    int FaturasVencidas,
    decimal TaxaConversao,
    decimal ReceitaPeriodo,
    decimal ValorVencido,
    decimal TicketMedio,
    double AtrasoMedioDias,
    IReadOnlyList<TopInadimplenteResult> TopInadimplentes,
    DateTime PeriodoInicio,
    DateTime PeriodoFim
);

/// <summary>
/// Compoe o snapshot de metricas financeiras a partir de queries agregadas
/// no <see cref="IFaturaRepository"/> e <see cref="IAssinaturaEmpresaRepository"/>.
///
/// <para>
/// F13 — cache via <see cref="ICacheService"/> com TTL 5 minutos. Chave
/// <c>metricas:{empresaId|null}:{dias}</c>. Sem cache, 6 queries SQL custavam
/// ~200ms em Postgres com indices. Com cache, &lt; 1ms na hit. Invalidacao
/// via <c>ForcarRefresh=true</c> (admin pode disparar pelo dashboard).
/// </para>
/// </summary>
public class MetricasFinanceirasUseCase(
    IFaturaRepository faturaRepo,
    IAssinaturaEmpresaRepository assinaturaRepo,
    ICacheService cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<MetricasFinanceirasResult> ExecuteAsync(
        MetricasFinanceirasCommand cmd, CancellationToken ct = default)
    {
        var dias = Math.Clamp(cmd.DiasRetroativo, 1, 365);
        var cacheKey = $"metricas:{cmd.EmpresaId?.ToString("N") ?? "all"}:{dias}";

        if (!cmd.ForcarRefresh)
        {
            var cached = await cache.GetAsync<MetricasFinanceirasResult>(cacheKey);
            if (cached is not null) return cached;
        }

        var result = await ComputarAsync(dias, cmd.EmpresaId, ct);
        await cache.SetAsync(cacheKey, result, CacheTtl);
        return result;
    }

    private async Task<MetricasFinanceirasResult> ComputarAsync(
        int dias, Guid? empresaId, CancellationToken ct)
    {
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
