using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class FinanceiroController(FinanceiroService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/financeiro")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Financeiro";
        ViewBag.ActiveMenuItem = "Financeiro";
        var result = await svc.ObterDashboardAsync();
        DashboardFinanceiroApi dashboard = result.Success && result.Data is not null
            ? result.Data
            : new DashboardFinanceiroApi();
        if (!result.Success && result.ErrorMessage is not null) Toast("error", result.ErrorMessage);
        return View(dashboard);
    }

    [HttpGet("/financeiro/fluxo-caixa")]
    public async Task<IActionResult> FluxoCaixa(string periodicidade = "Mensal", DateTime? inicio = null, DateTime? fim = null)
    {
        ViewBag.Title = "Fluxo de Caixa";
        ViewBag.ActiveMenuItem = "Financeiro";
        ViewBag.Periodicidade = periodicidade;

        var iniDef = inicio ?? new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        var fimDef = fim ?? iniDef.AddMonths(6).AddDays(-1);
        ViewBag.Inicio = iniDef.ToString("yyyy-MM-dd");
        ViewBag.Fim = fimDef.ToString("yyyy-MM-dd");

        var result = await svc.ObterFluxoCaixaAsync(periodicidade, iniDef, fimDef);
        var buckets = result.Success && result.Data is not null ? result.Data : new List<FluxoBucketApi>();
        if (!result.Success && result.ErrorMessage is not null) Toast("error", result.ErrorMessage);
        return View(buckets);
    }
}
