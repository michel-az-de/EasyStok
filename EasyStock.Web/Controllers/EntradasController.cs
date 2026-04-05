using EasyStock.Web.Models.ViewModels.Entradas;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class EntradasController(EntradasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/entradas/nova")]
    public IActionResult Nova()
    {
        ViewBag.Title = "Nova Entrada";
        ViewBag.ActiveMenuItem = "Entradas";
        return View(new EntradaFormViewModel());
    }

    [HttpPost("/entradas/nova")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(EntradaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Nova Entrada";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Nova", vm);
        }

        var result = await svc.CriarEntradaAsync(vm);
        if (HasError(result))
        {
            ViewBag.Title = "Nova Entrada";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Nova", vm);
        }

        Toast("success", "Entrada registrada com sucesso!");
        return RedirectToAction("Index", "Estoque");
    }

    [HttpGet("/entradas/reposicao")]
    public IActionResult Reposicao()
    {
        ViewBag.Title = "Reposição Rápida";
        ViewBag.ActiveMenuItem = "Entradas";
        return View(new EntradaFormViewModel());
    }

    [HttpPost("/entradas/reposicao")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SalvarReposicao(EntradaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Reposição Rápida";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Reposicao", vm);
        }

        var result = await svc.ReposicaoAsync(vm);
        if (HasError(result))
        {
            ViewBag.Title = "Reposição Rápida";
            ViewBag.ActiveMenuItem = "Entradas";
            return View("Reposicao", vm);
        }

        Toast("success", "Reposição registrada com sucesso!");
        return RedirectToAction("Index", "Estoque");
    }

    [HttpGet("/entradas/historico")]
    public async Task<IActionResult> Historico(int page = 1, string? tipo = null, string? periodoInicio = null, string? periodoFim = null)
    {
        ViewBag.Title = "Histórico de Entradas";
        ViewBag.ActiveMenuItem = "Entradas";

        var result = await svc.HistoricoAsync(page, tipo, periodoInicio, periodoFim);
        if (HasError(result)) return View(new EntradasHistoricoViewModel());

        var paged = result.Data!;
        var vm = new EntradasHistoricoViewModel
        {
            Entradas = paged.Data,
            Tipo = tipo,
            PeriodoInicio = periodoInicio,
            PeriodoFim = periodoFim,
            Paginacao = new PaginationViewModel
            {
                Page = paged.Meta.Page,
                Pages = paged.Meta.Pages,
                Total = paged.Meta.Total,
                Limit = paged.Meta.Limit
            }
        };
        return View(vm);
    }
}
