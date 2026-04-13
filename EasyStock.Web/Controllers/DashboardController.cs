using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Dashboard;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class DashboardController(ApiClient api, SessionService session) : BaseController(session)
{
    [HttpGet("/dashboard")]
    [HttpGet("/")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Dashboard";
        ViewBag.ActiveMenuItem = "Dashboard";

        var vm = new DashboardViewModel();

        // Load all dashboard data in parallel — best-effort, show empty state on failure
        var dashTask = api.GetAsync<DashboardResumoApi>("analytics/dashboard");
        var reposTask = api.GetAsync<List<ReposicaoSugerida>>("analytics/reposicao");
        var movsTask = api.GetAsync<List<MovimentacaoResumo>>("analytics/movimentacoes?diasPadrao=30");
        var receitaTask = api.GetAsync<List<ReceitaPorPeriodoApi>>("analytics/receita?meses=6");

        await Task.WhenAll(dashTask, reposTask, movsTask, receitaTask);

        var (dashResult, reposResult, movsResult, receitaResult) = (
            dashTask.Result, reposTask.Result, movsTask.Result, receitaTask.Result
        );

        if (dashResult.Success && dashResult.Data is { } d)
        {
            vm.TotalProdutos = d.TotalSkus;
            vm.QuantidadeTotalEmEstoque = d.QuantidadeTotalEmEstoque;
            vm.ValorEstoque = d.ValorTotalEstoque;
            vm.ReceitaMes = d.ReceitaEstimadaPeriodo;
            vm.EstoqueCritico = d.AlertasEstoqueBaixo;
            vm.ProximosVencimento = d.AlertasVencimento;
            vm.ProdutosParados = d.AlertasItensParados;
        }

        if (reposResult.Success && reposResult.Data is { } repos)
            vm.SugestoesReposicao = repos.Count;

        if (movsResult.Success && movsResult.Data is { } movs)
        {
            foreach (var m in movs.OrderByDescending(x => x.Ano).ThenByDescending(x => x.Mes).ThenByDescending(x => x.Dia).Take(10))
            {
                vm.MovimentacoesRecentes.Add(new MovimentacaoRecente
                {
                    Tipo = m.Tipo,
                    TotalMovimentacoes = m.TotalMovimentacoes,
                    Qty = m.QuantidadeTotal,
                    Valor = m.ValorTotal > 0 ? m.ValorTotal : null,
                    Data = new DateOnly(m.Ano, m.Mes, m.Dia)
                });
            }
        }

        if (receitaResult.Success && receitaResult.Data is { } receita)
        {
            var ordenado = receita.OrderBy(r => r.Ano).ThenBy(r => r.Mes).ToList();
            vm.GraficoLabels = ordenado.Select(r => $"{r.Mes:D2}/{r.Ano}").ToList();
            vm.GraficoDados = ordenado.Select(r => r.ReceitaBruta).ToList();
        }

        return View(vm);
    }
}

