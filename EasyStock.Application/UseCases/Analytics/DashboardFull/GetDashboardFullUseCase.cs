using System.Diagnostics;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.Common;
using Microsoft.Extensions.Logging;

namespace EasyStock.Application.UseCases.Analytics.DashboardFull;

public class GetDashboardFullUseCase(
    IAnalyticsRepository analyticsRepository,
    ILogger<GetDashboardFullUseCase> logger)
{
    public async Task<DashboardFullResult> ExecuteAsync(GetDashboardFullCommand cmd)
    {
        UseCaseGuards.EnsureEmpresaId(cmd.EmpresaId);

        var stopwatch = Stopwatch.StartNew();

        var now = DateTime.UtcNow.AddMinutes(-cmd.TimezoneOffsetMinutes);
        var ate = now;
        var de = ate.AddDays(-cmd.PeriodoDias);
        var dePrev = de.AddDays(-cmd.PeriodoDias);

        // BUG 1 (diag): cliente reportou que periodo=365 zera Receita/Pedidos/Lotes/
        // ClientesAtivos enquanto periodo=30 retorna valores. Log inclui janela e
        // contadores pra correlacionar com dados do banco. Remover quando o bug
        // for confirmado/corrigido.
        logger.LogInformation(
            "Dashboard full request | empresa={EmpresaId} loja={LojaId} periodoDias={PeriodoDias} tz={Tz} de={De:o} ate={Ate:o}",
            cmd.EmpresaId, cmd.LojaId, cmd.PeriodoDias, cmd.TimezoneOffsetMinutes, de, ate);

        // DbContext scoped não é thread-safe. Queries executadas sequencialmente.
        // A maioria responde do cache Redis (5min TTL) então o custo total fica baixo.
        var kpis = await analyticsRepository.GetDashboardKpisAsync(cmd.EmpresaId, de, ate, cmd.LojaId);
        var kpisPrev = await analyticsRepository.GetDashboardKpisAsync(cmd.EmpresaId, dePrev, de, cmd.LojaId);

        // BUG 1 (diag): se algum KPI principal vier zerado em periodo > 90 dias
        // (zona suspeita), logamos como Warning pra subir o sinal no observability.
        if (cmd.PeriodoDias > 90 &&
            (kpis.Receita == 0 || kpis.Pedidos == 0 || kpis.LotesProduzidos == 0 || kpis.ClientesAtivos == 0))
        {
            logger.LogWarning(
                "Dashboard KPIs suspeitos (zerados em periodo grande) | periodoDias={PeriodoDias} receita={Receita} pedidos={Pedidos} lotes={Lotes} clientes={Clientes} de={De:o} ate={Ate:o}",
                cmd.PeriodoDias, kpis.Receita, kpis.Pedidos, kpis.LotesProduzidos, kpis.ClientesAtivos, de, ate);
        }
        var estoqueStatus = await analyticsRepository.GetEstoqueStatusDistribuicaoAsync(cmd.EmpresaId, cmd.LojaId);
        var pendentes = await analyticsRepository.GetPedidosPendentesAsync(cmd.EmpresaId, cmd.PeriodoDias, cmd.LojaId);
        var (alertasValidade, _) = await analyticsRepository.GetAlertasValidadeAsync(cmd.EmpresaId, 30, 1, 10, cmd.LojaId);
        var (alertasParados, _) = await analyticsRepository.GetItensParadosDetalhadosAsync(cmd.EmpresaId, 30, 1, 10, cmd.LojaId);
        var alertasCriticos = await analyticsRepository.GetItensCriticosResumoAsync(cmd.EmpresaId, 10, cmd.LojaId);
        var entreguesSemVenda = await analyticsRepository.GetEntreguesSemVendaCountAsync(cmd.EmpresaId, cmd.PeriodoDias, cmd.LojaId);
        var fornecedores = await analyticsRepository.GetFornecedoresResumoAsync(cmd.EmpresaId, cmd.LojaId);

        var delta = CalculateDelta(kpis, kpisPrev);

        // Críticos primeiro, depois vencimento, depois parados — ordem de urgência
        var alertas = new List<AlertaEstoqueResumo>();
        foreach (var c in alertasCriticos)
            alertas.Add(c);
        foreach (var a in alertasValidade)
            alertas.Add(new AlertaEstoqueResumo(a.ItemEstoqueId, a.ProdutoId, a.NomeProduto, "vencimento", a.QuantidadeAtual, a.DiasAteVencimento));
        foreach (var p in alertasParados)
            alertas.Add(new AlertaEstoqueResumo(p.ItemEstoqueId, p.ProdutoId, p.NomeProduto, "parado", p.QuantidadeAtual, p.DiasSemMovimentacao));

        var insights = GenerateInsights(kpis, delta, estoqueStatus, pendentes.Count, fornecedores.Ativos > 0);
        var pendentesTotal = pendentes.Sum(p => p.EmAberto);

        stopwatch.Stop();
        logger.LogInformation("Dashboard full retrieved in {Ms}ms for empresa {EmpresaId}",
            stopwatch.ElapsedMilliseconds, cmd.EmpresaId);

        return new DashboardFullResult(kpis, delta, estoqueStatus, pendentes, pendentesTotal, alertas, entreguesSemVenda, insights);
    }

    private static DashboardKpisDelta CalculateDelta(DashboardKpis current, DashboardKpis previous)
    {
        // null = "sem base de comparação" (período anterior vazio). A view renderiza "—" em vez de
        // confundir o usuário com "+100%" quando o valor anterior era zero.
        return new DashboardKpisDelta(
            Receita: CalcPct(current.Receita, previous.Receita),
            TicketMedio: CalcPct(current.TicketMedio, previous.TicketMedio),
            Pedidos: CalcPct(current.Pedidos, previous.Pedidos),
            ItensEmEstoque: CalcPct(current.ItensEmEstoque, previous.ItensEmEstoque),
            CustoEstoque: CalcPct(current.CustoEstoque, previous.CustoEstoque),
            MargemBruta: (previous.Receita == 0 || current.MargemBruta is null || previous.MargemBruta is null)
                ? null
                : Math.Round(current.MargemBruta.Value - previous.MargemBruta.Value, 1),
            LotesProduzidos: CalcPct(current.LotesProduzidos, previous.LotesProduzidos),
            ClientesAtivos: CalcPct(current.ClientesAtivos, previous.ClientesAtivos));
    }

    private static decimal? CalcPct(decimal current, decimal previous)
    {
        if (previous == 0) return null;
        return Math.Round((current - previous) / previous * 100m, 1);
    }

    private static List<InsightDto> GenerateInsights(DashboardKpis kpis, DashboardKpisDelta delta,
        EstoqueStatusDistribuicao estoque, int pendentesCount, bool temFornecedoresAtivos)
    {
        var insights = new List<InsightDto>();

        if (delta.Receita is decimal dR && dR < -15m)
            insights.Add(new InsightDto("receita", "alert", $"Receita caiu {Math.Abs(dR):0.#}% em relação ao período anterior."));
        else if (delta.Receita is decimal dRp && dRp > 15m)
            insights.Add(new InsightDto("receita", "positive", $"Receita subiu {dRp:0.#}%! Bom período para o negócio."));

        if (kpis.MargemBruta is decimal mB && mB > 0 && mB < 20m)
            insights.Add(new InsightDto("margem", "warning", "Margem baixa. Revise seus custos ou ajuste preços de venda."));

        if (estoque.Total > 0 && estoque.Critico > 0)
        {
            var pctCritico = (decimal)estoque.Critico / estoque.Total * 100m;
            if (pctCritico > 20)
            {
                // Esclarece a unidade: "lotes" (rows de ItemEstoque ativas) vs
                // "itens" (soma de quantidade). Antes só falava "estoque" e
                // confundia com o card "Itens em estoque" do topo.
                var lotesLabel = estoque.Total == 1 ? "lote ativo está crítico" : "lotes ativos estão críticos";
                var msg = temFornecedoresAtivos
                    ? $"{pctCritico:0}% dos {lotesLabel}. Considere repor agora."
                    : $"{pctCritico:0}% dos {lotesLabel} — cadastre um fornecedor para conseguir repor.";
                insights.Add(new InsightDto("estoque", "alert", msg));
            }
        }

        if (pendentesCount > 0)
        {
            var pedidoLabel = pendentesCount == 1 ? "pedido aguardando pagamento" : "pedidos aguardando pagamento";
            insights.Add(new InsightDto("pedidos", "warning", $"{pendentesCount} {pedidoLabel}."));
        }

        if (delta.ClientesAtivos is decimal dC && dC > 20m)
            insights.Add(new InsightDto("clientes", "positive", $"Base de clientes cresceu {dC:0.#}% no período."));

        return insights;
    }
}
