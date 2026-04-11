using System.Diagnostics;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

[AllowAnonymous]
public class DiagnosticoController(DiagnosticoWebService diagnosticoService, SessionService session) : Controller
{
    [Route("diagnostico")]
    public async Task<IActionResult> Index()
    {
        var sw = Stopwatch.StartNew();
        var (apiResult, latenciaApiMs) = await diagnosticoService.ObterDiagnosticoComLatenciaAsync();
        sw.Stop();

        var apiAlcancavel = apiResult is not null;

        ViewBag.ApiAlcancavel = apiAlcancavel;
        ViewBag.ApiBaseUrl = diagnosticoService.ApiBaseUrl;
        ViewBag.Timestamp = DateTimeOffset.UtcNow;
        ViewBag.LatenciaApiMs = latenciaApiMs;

        // Busca logs se o usuário estiver autenticado como Admin/SuperAdmin
        if (User.Identity?.IsAuthenticated == true &&
            (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
        {
            var token = session.GetToken();
            if (!string.IsNullOrEmpty(token))
            {
                var logs = await diagnosticoService.FetchLogsAsync(token, 100);
                ViewBag.Logs = logs;
                ViewBag.IsAdmin = true;
            }
        }

        return View(apiResult);
    }

    [Route("diagnostico/json")]
    public async Task<IActionResult> Json()
    {
        var (apiResult, latenciaApiMs) = await diagnosticoService.ObterDiagnosticoComLatenciaAsync();
        return base.Json(new
        {
            web = new { status = "ok", timestamp = DateTimeOffset.UtcNow },
            apiAlcancavel = apiResult is not null,
            apiBaseUrl = diagnosticoService.ApiBaseUrl,
            latenciaApiMs,
            api = apiResult
        });
    }
}
