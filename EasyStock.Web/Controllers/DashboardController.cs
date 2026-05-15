using EasyStock.Web.Models.Api;
using EasyStock.Web.Models.ViewModels.Dashboard;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class DashboardController(ApiClient api, SessionService session) : BaseController(session)
{
    [HttpGet("/dashboard")]
    [HttpGet("/")]
    public async Task<IActionResult> Index(int meses = 6)
    {
        ViewBag.Title = "Dashboard";
        ViewBag.ActiveMenuItem = "Dashboard";
        if (meses is not (3 or 6 or 12)) meses = 6;
        ViewBag.MesesGrafico = meses;

        var vm = new DashboardViewModel();

        // Load all dashboard data in parallel — best-effort, show empty state on failure.
        // Usa await direto em cada task iniciada para evitar .Result bloqueando thread.
        var dashTask = api.GetAsync<DashboardResumoApi>("analytics/dashboard");
        var reposTask = api.GetAsync<List<ReposicaoSugerida>>("analytics/reposicao");
        var movsTask = api.GetAsync<List<MovimentacaoResumo>>("analytics/movimentacoes?diasPadrao=30");
        var receitaTask = api.GetAsync<List<ReceitaPorPeriodoApi>>($"analytics/receita?meses={meses}");
        var iaUsoTask = api.GetAsync<IaUsoApi>("ia/uso");

        var dashResult = await dashTask;
        var reposResult = await reposTask;
        var movsResult = await movsTask;
        var receitaResult = await receitaTask;
        var iaUsoResult = await iaUsoTask;

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
                    Valor = m.ValorTotal > 0 ? m.ValorTotal : (decimal?)0,
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

        if (iaUsoResult.Success && iaUsoResult.Data is { } ia)
        {
            vm.IaConfigurada = true;
            vm.IaIlimitada = ia.Ilimitado;
            vm.GeracoesIaUsadas = ia.TotalGeracoes;
            vm.GeracoesIaLimite = ia.LimiteMensal ?? 0;
        }

        return View(vm);
    }
}

