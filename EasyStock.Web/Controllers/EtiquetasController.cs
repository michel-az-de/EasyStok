using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class EtiquetasController(SessionService session, EtiquetasService etiquetas) : BaseController(session)
{
    [HttpGet("/etiquetas/modelos")]
    public IActionResult Modelos()
    {
        ViewBag.Title          = "Modelos de etiqueta";
        ViewBag.ActiveMenuItem = "Lotes";
        ViewBag.EmpresaId      = Session.GetEmpresaId();
        return View();
    }

    [HttpGet("/etiquetas/editor/{id}")]
    public async Task<IActionResult> Editor(string id)
    {
        var result = await etiquetas.ObterAsync("Empresa", id);
        if (!result.Success || result.Data is null) return RedirectToAction(nameof(Modelos));

        ViewBag.Title          = $"Editar — {result.Data.Nome}";
        ViewBag.ActiveMenuItem = "Lotes";
        ViewBag.TemplateId     = id;
        ViewBag.LayoutJson     = result.Data.LayoutJson;
        ViewBag.EmpresaId      = Session.GetEmpresaId();
        ViewBag.Nome           = result.Data.Nome;
        return View();
    }
}
