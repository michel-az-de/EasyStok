using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

[AllowAnonymous]
public class DiagnosticoController(DiagnosticoWebService diagnosticoService) : Controller
{
    [Route("diagnostico")]
    public async Task<IActionResult> Index()
    {
        var apiResult = await diagnosticoService.ObterDiagnosticoAsync();
        var apiAlcancavel = apiResult is not null;

        ViewBag.ApiAlcancavel = apiAlcancavel;
        ViewBag.ApiBaseUrl = diagnosticoService.ApiBaseUrl;
        ViewBag.Timestamp = DateTimeOffset.UtcNow;

        return View(apiResult);
    }

    [Route("diagnostico/json")]
    public async Task<IActionResult> Json()
    {
        var apiResult = await diagnosticoService.ObterDiagnosticoAsync();
        return base.Json(new
        {
            web = new { status = "ok", timestamp = DateTimeOffset.UtcNow },
            apiAlcancavel = apiResult is not null,
            apiBaseUrl = diagnosticoService.ApiBaseUrl,
            api = apiResult
        });
    }
}
