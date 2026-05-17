using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class RelatoriosController(RelatoriosService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/relatorios")]
    public async Task<IActionResult> Index(string? categoria = null, string? status = null, int page = 1)
    {
        ViewBag.Title = "Relatórios";
        ViewBag.ActiveMenuItem = "Relatorios";
        ViewBag.FiltroCategoria = categoria;
        ViewBag.FiltroStatus = status;
        ViewBag.Page = page;

        var take = 25;
        var skip = (Math.Max(1, page) - 1) * take;

        var catalogTask = svc.CatalogAsync(categoria);
        var runsTask = svc.ListRunsAsync(categoria, status, skip, take);

        var catalogResult = await catalogTask;
        var runsResult = await runsTask;

        ViewBag.Catalog = catalogResult.Success ? catalogResult.Data ?? [] : new List<ReportCatalogItemApi>();
        ViewBag.Runs = runsResult.Success ? runsResult.Data?.Items ?? [] : new List<ReportRunApi>();
        ViewBag.RunsTotal = runsResult.Success ? runsResult.Data?.Total ?? 0 : 0;

        return View();
    }

    [HttpGet("/relatorios/run/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id)
    {
        ViewBag.Title = "Detalhe do Relatório";
        ViewBag.ActiveMenuItem = "Relatorios";

        var result = await svc.GetRunAsync(id);
        if (!result.Success || result.Data is null)
        {
            Toast("error", "Execução de relatório não encontrada.");
            return RedirectToAction(nameof(Index));
        }

        ViewBag.Run = result.Data;
        return View();
    }

    [HttpPost("/relatorios/enqueue")]
    public async Task<IActionResult> Enqueue(string key, string paramsJson, string format)
    {
        var result = await svc.EnqueueAsync(key, paramsJson ?? "{}", format ?? "Xlsx");
        if (HasErrorVerbose(result, "Enfileirar relatório"))
            return RedirectToAction(nameof(Index));

        Toast("success", $"Relatório enfileirado. Acompanhe o progresso abaixo.");
        return RedirectToAction(nameof(Detail), new { id = result.Data!.Id });
    }

    [HttpPost("/relatorios/cancel/{id:guid}")]
    public async Task<IActionResult> Cancel(Guid id)
    {
        var result = await svc.CancelRunAsync(id);
        if (!result.Success)
            Toast("warning", "Não foi possível cancelar.");
        else
            Toast("success", "Relatório cancelado.");

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/relatorios/repeat/{id:guid}")]
    public async Task<IActionResult> Repeat(Guid id)
    {
        var result = await svc.RepeatRunAsync(id);
        if (HasErrorVerbose(result, "Repetir relatório"))
            return RedirectToAction(nameof(Index));

        Toast("success", "Relatório re-enfileirado.");
        return RedirectToAction(nameof(Detail), new { id = result.Data!.Id });
    }
}
