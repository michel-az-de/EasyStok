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
}
