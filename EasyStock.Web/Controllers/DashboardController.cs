using System.Text.Json;
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

        // Load dashboard data — best-effort, show empty state on failure
        var dashResult = await api.GetAsync<JsonElement>("analytics/dashboard");
        if (dashResult.Success)
        {
            var d = dashResult.Data;
            vm.TotalProdutos = GetInt(d, "totalSkus");
            vm.ReceitaMes = GetDecimal(d, "receitaEstimadaPeriodo");
            vm.EstoqueCritico = GetInt(d, "alertasEstoqueBaixo");
            vm.ProximosVencimento = GetInt(d, "alertasVencimento");
            vm.ProdutosParados = GetInt(d, "alertasItensParados");
        }

        return View(vm);
    }

    private static int GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static decimal GetDecimal(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetDecimal(out var d) ? d : 0m;
}
