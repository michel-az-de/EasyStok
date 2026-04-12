using System.Diagnostics;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class DiagnosticoController(DiagnosticoWebService diagnosticoService, SessionService session) : Controller
{
    [AllowAnonymous]
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

        if (User.Identity?.IsAuthenticated == true &&
            (User.IsInRole("Admin") || User.IsInRole("SuperAdmin")))
        {
            ViewBag.IsAdmin = true;
        }

        return View(apiResult);
    }

    [AllowAnonymous]
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

    // ──────────────────────────────────────────────────────────────────────
    // Proxy endpoints para Alpine.js (async client-side loading)
    // ──────────────────────────────────────────────────────────────────────

    [AllowAnonymous]
    [Route("diagnostico/api/endpoints")]
    public async Task<IActionResult> ProxyEndpoints()
    {
        var result = await diagnosticoService.FetchEndpointTestsAsync();
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/historico")]
    public async Task<IActionResult> ProxyHistorico()
    {
        var result = await diagnosticoService.FetchHealthHistoryAsync();
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Route("diagnostico/api/logs-enhanced")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ProxyEnhancedLogs([FromQuery] int hours = 48)
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await diagnosticoService.FetchEnhancedLogsAsync(token, hours);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível obter logs da API" });
        return base.Json(result);
    }

    [HttpPost]
    [Route("diagnostico/api/logs/limpar")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ProxyLimparLogs()
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await diagnosticoService.LimparLogsAsync(token);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Route("diagnostico/api/logs/exportar")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ProxyExportarLogs([FromQuery] int hours = 48)
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var (stream, fileName) = await diagnosticoService.ExportarLogsAsync(token, hours);
        if (stream is null)
            return StatusCode(502, new { error = "Não foi possível obter logs da API" });

        return File(stream, "text/plain; charset=utf-8", fileName ?? $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.log");
    }

    [HttpPost]
    [Route("diagnostico/api/logs/salvar-storage")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> ProxySalvarStorage()
    {
        var token = session.GetToken();
        if (string.IsNullOrEmpty(token))
            return Unauthorized();

        var result = await diagnosticoService.SalvarLogsStorageAsync(token);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }
}
