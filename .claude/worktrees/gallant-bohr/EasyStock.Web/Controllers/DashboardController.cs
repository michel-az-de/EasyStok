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
            vm.TotalProdutos = GetInt(d, "totalProdutos");
            vm.EntradasMes = GetInt(d, "entradasMes");
            vm.VendasMes = GetInt(d, "vendasMes");
            vm.ReceitaMes = GetDecimal(d, "receitaMes");
            vm.EstoqueCritico = GetInt(d, "estoqueCritico");
            vm.ProximosVencimento = GetInt(d, "proximosVencimento");
            vm.ProdutosParados = GetInt(d, "produtosParados");
            vm.SugestoesReposicao = GetInt(d, "sugestoesReposicao");

            if (d.TryGetProperty("grafico", out var g))
            {
                if (g.TryGetProperty("labels", out var labels))
                    vm.GraficoLabels = labels.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                if (g.TryGetProperty("dados", out var dados))
                    vm.GraficoDados = dados.EnumerateArray().Select(x => x.GetDecimal()).ToList();
            }

            if (d.TryGetProperty("movimentacoesRecentes", out var movs))
            {
                vm.MovimentacoesRecentes = movs.EnumerateArray().Select(m => new MovimentacaoRecente
                {
                    ProdutoNome = GetString(m, "produtoNome") ?? "-",
                    Tipo = GetString(m, "tipo") ?? "-",
                    Qty = GetInt(m, "qty"),
                    Data = GetDateOnly(m, "data")
                }).ToList();
            }
        }

        return View(vm);
    }

    private static string? GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetInt32(out var i) ? i : 0;

    private static decimal GetDecimal(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.TryGetDecimal(out var d) ? d : 0m;

    private static DateOnly GetDateOnly(JsonElement el, string prop)
    {
        if (el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
        {
            if (DateOnly.TryParse(v.GetString(), out var d)) return d;
        }
        return DateOnly.FromDateTime(DateTime.Today);
    }
}
