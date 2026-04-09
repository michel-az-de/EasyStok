using EasyStock.Web.Models.ViewModels.Saidas;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class SaidasController(SaidasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/saidas")]
    [HttpGet("/saidas/nova")]
    public IActionResult Nova()
    {
        ViewBag.Title = "Nova Saída";
        ViewBag.ActiveMenuItem = "Saidas";
        return View(new SaidaFormViewModel());
    }

    [HttpPost("/saidas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(SaidaFormViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Nova Saída";
            ViewBag.ActiveMenuItem = "Saidas";
            return View("Nova", vm);
        }

        var result = await svc.CriarAsync(vm);
        if (HasError(result))
        {
            ViewBag.Title = "Nova Saída";
            ViewBag.ActiveMenuItem = "Saidas";
            return View("Nova", vm);
        }

        Toast("success", "Saída registrada com sucesso!");
        return RedirectToAction(nameof(Historico));
    }

    [HttpGet("/saidas/historico")]
    public async Task<IActionResult> Historico(
        int page = 1, string? natureza = null, string? de = null, string? ate = null)
    {
        ViewBag.Title = "Histórico de Saídas";
        ViewBag.ActiveMenuItem = "Saidas";

        var result = await svc.ListarAsync(page, natureza, de, ate);
        var vm = new SaidasHistoricoViewModel
        {
            FiltroNatureza = natureza,
            PeriodoInicio = de,
            PeriodoFim = ate
        };

        if (!result.Success)
        {
            HasError(result);
            return View(vm);
        }

        var paged = result.Data!;
        vm.Itens = paged.Data;
        vm.TotalRegistros = paged.Meta.Total;
        vm.Paginacao = new Models.ViewModels.Shared.PaginationViewModel
        {
            Page = paged.Meta.Page,
            Pages = paged.Meta.Pages,
            Total = paged.Meta.Total,
            Limit = paged.Meta.Limit
        };

        // Compute KPIs
        vm.TotalUnidades = vm.Itens.Sum(s => s.Qty);
        vm.ReceitaTotal = vm.Itens.Where(s => s.ValorTotal is not null).Sum(s => s.ValorTotal!.Valor);
        vm.TotalVendas = vm.Itens.Count(s => s.Natureza.Equals("Venda", StringComparison.OrdinalIgnoreCase));
        vm.TotalPerdas = vm.Itens.Count(s => s.Natureza.Equals("Perda", StringComparison.OrdinalIgnoreCase));

        return View(vm);
    }

    [HttpPost("/saidas/{id}/estornar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Estornar(string id)
    {
        var result = await svc.EstornarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Historico));

        Toast("success", "Saída estornada com sucesso!");
        return RedirectToAction(nameof(Historico));
    }
}
