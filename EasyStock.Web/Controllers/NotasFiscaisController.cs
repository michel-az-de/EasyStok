using EasyStock.Web.Models.ViewModels.NotasFiscais;
using EasyStock.Web.Models.ViewModels.Shared;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class NotasFiscaisController(NotasFiscaisService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/notas-fiscais")]
    public async Task<IActionResult> Index(string? status, string? desde, string? ate, string? search, int page = 1)
    {
        ViewBag.ActiveMenuItem = "NotasFiscais";
        ViewBag.Title = "Notas Fiscais";

        var result = await svc.ListarAsync(page, status, desde, ate, search);
        if (HasError(result)) return View(new NotasFiscaisListViewModel());

        var vm = new NotasFiscaisListViewModel
        {
            Itens = result.Data?.Data ?? [],
            Paginacao = result.Data?.Meta is { } meta
                ? new PaginationViewModel
                {
                    Page = meta.Page,
                    Pages = meta.Pages,
                    Total = meta.Total,
                    Limit = meta.Limit,
                }
                : new PaginationViewModel(),
            FiltroStatus = status,
            PeriodoInicio = desde,
            PeriodoFim = ate,
            Busca = search,
        };

        return View(vm);
    }

    [HttpGet("/notas-fiscais/{id:guid}")]
    public async Task<IActionResult> Detalhes(Guid id)
    {
        ViewBag.ActiveMenuItem = "NotasFiscais";
        ViewBag.Title = "Detalhe da Nota Fiscal";

        var result = await svc.ObterAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        var d = result.Data!;
        var vm = new NotaFiscalDetalheViewModel
        {
            Nfe = d.Nfe,
            Itens = d.Itens,
            Eventos = d.Eventos,
        };

        return View(vm);
    }

    [HttpPost("/notas-fiscais/{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id, string motivo)
    {
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 15)
        {
            Toast("error", "Motivo do cancelamento deve ter pelo menos 15 caracteres.");
            return RedirectToAction(nameof(Detalhes), new { id });
        }

        var result = await svc.CancelarAsync(id, motivo);
        if (HasErrorVerbose(result, "Cancelamento"))
            return RedirectToAction(nameof(Detalhes), new { id });

        Toast("success", "Nota cancelada com sucesso. O protocolo de cancelamento foi registrado.");
        return RedirectToAction(nameof(Detalhes), new { id });
    }
}
