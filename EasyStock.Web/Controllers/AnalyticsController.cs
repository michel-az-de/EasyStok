using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Analytics;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AnalyticsController(AnalyticsService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/analytics")]
    public async Task<IActionResult> Index(int meses = 6)
    {
        ViewBag.Title = "Analytics";
        ViewBag.ActiveMenuItem = "Analytics";
        if (meses is not (3 or 6 or 12)) meses = 6;
        ViewBag.MesesGrafico = meses;

        var vm = new AnalyticsViewModel();

        var (dashTask, reposTask, alertasTask, receitaTask) = (
            svc.DashboardAsync(),
            svc.ReposicaoAsync(),
            svc.AlertasAsync(),
            svc.ReceitaAsync(meses)
        );
        var (dashResult, reposResult, alertasResult, receitaResult) =
            (await dashTask, await reposTask, await alertasTask, await receitaTask);

        if (dashResult.Success && dashResult.Data is { } dash)
        {
            vm.ReceitaEstimadaPeriodo = dash.ReceitaEstimadaPeriodo;
            vm.TotalEstoque = dash.QuantidadeTotalEmEstoque;
            vm.ValorEstoque = dash.ValorTotalEstoque;
            vm.MediaVendasDiaria = dash.MediaVendasDiaria;

            vm.ProjUnidadesDia = dash.MediaVendasDiaria;
            vm.ProjUnidades7d = dash.MediaVendasDiaria * 7;
            vm.ProjUnidades30d = dash.MediaVendasDiaria * 30;
            vm.ProjReceita30d = dash.ReceitaEstimadaPeriodo;
            vm.ReceitaProjetadaDisponivel = dash.ReceitaEstimadaPeriodo > 0;
        }

        if (receitaResult.Success && receitaResult.Data is { } receita && receita.Count > 0)
        {
            var ultimo = receita.OrderByDescending(r => r.Ano).ThenByDescending(r => r.Mes).First();
            vm.UnidadesVendidasMes = ultimo.TotalItensVendidos;
            vm.ReceitaMes = ultimo.ReceitaBruta;

            var ordenado = receita.OrderBy(r => r.Ano).ThenBy(r => r.Mes).ToList();
            vm.GraficoLabels = ordenado.Select(r => $"{r.Mes:D2}/{r.Ano}").ToList();
            vm.GraficoDados = ordenado.Select(r => r.ReceitaBruta).ToList();
        }

        if (reposResult.Success)
            vm.ItensReposicaoUrgente = reposResult.Data ?? [];

        if (alertasResult.Success && alertasResult.Data is { } alertas)
        {
            vm.Alertas = alertas
                .GroupBy(a => a.ProdutoId)
                .Select(g =>
                {
                    var minDias = g.Min(a => a.DiasAteVencimento);
                    var totalQtd = g.Sum(a => a.QuantidadeAtual);
                    var lotes = g.Count();
                    var nome = g.First().NomeProduto ?? g.First().CodigoInterno ?? "Produto";
                    var lotesStr = lotes > 1 ? $" em {lotes} lotes" : "";
                    return new AlertaItem(
                        "validade",
                        nome,
                        $"Vence em {minDias} dia(s) — {totalQtd} un. em risco{lotesStr}",
                        g.Key
                    );
                })
                .OrderBy(a => int.TryParse(
                    System.Text.RegularExpressions.Regex.Match(a.Mensagem, @"\d+").Value, out var d) ? d : int.MaxValue)
                .ToList();
        }

        return View(vm);
    }

    [HttpGet("/analytics/movimentacoes")]
    public async Task<IActionResult> Movimentacoes(
        int page = 1, string? tipo = null, string? de = null, string? ate = null)
    {
        ViewBag.Title = "Movimentações";
        ViewBag.ActiveMenuItem = "Movimentacoes";

        var result = await svc.MovimentacoesAsync(tipo, de, ate);
        var vm = new MovimentacoesViewModel
        {
            FiltroTipo = tipo,
            PeriodoInicio = de,
            PeriodoFim = ate
        };

        if (result.Success && result.Data is { } movs)
        {
            foreach (var m in movs)
            {
                vm.Itens.Add(new MovimentacaoItem
                {
                    Tipo = m.Tipo,
                    Resumo = $"{m.TotalMovimentacoes} movimentação(ões)",
                    Qty = m.QuantidadeTotal,
                    Valor = m.ValorTotal > 0 ? m.ValorTotal : null,
                    Data = new DateOnly(m.Ano, m.Mes, m.Dia)
                });
            }
        }

        return View(vm);
    }
}
