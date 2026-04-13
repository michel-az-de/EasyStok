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

        ViewBag.IsAdmin = true; // Diagnóstico é público — todas as funcionalidades liberadas

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

    [AllowAnonymous]
    [Route("diagnostico/api/logs-enhanced")]
    public async Task<IActionResult> ProxyEnhancedLogs([FromQuery] int hours = 48)
    {
        var result = await diagnosticoService.FetchEnhancedLogsAsync(null, hours);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível obter logs da API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("diagnostico/api/logs/limpar")]
    public async Task<IActionResult> ProxyLimparLogs()
    {
        var result = await diagnosticoService.LimparLogsAsync(null);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/logs/exportar")]
    public async Task<IActionResult> ProxyExportarLogs([FromQuery] int hours = 48)
    {
        var (stream, fileName) = await diagnosticoService.ExportarLogsAsync(null, hours);
        if (stream is null)
            return StatusCode(502, new { error = "Não foi possível obter logs da API" });

        return File(stream, "text/plain; charset=utf-8", fileName ?? $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.log");
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("diagnostico/api/logs/salvar-storage")]
    public async Task<IActionResult> ProxySalvarStorage()
    {
        var result = await diagnosticoService.SalvarLogsStorageAsync(null);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    // ── Novos proxies ──────────────────────────────────────────────────────

    [AllowAnonymous]
    [Route("diagnostico/api/logs/lixeira")]
    public async Task<IActionResult> ProxyLixeira()
    {
        var result = await diagnosticoService.FetchLixeiraAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("diagnostico/api/logs/lixeira/esvaziar")]
    public async Task<IActionResult> ProxyEsvaziarLixeira()
    {
        var result = await diagnosticoService.EsvaziarLixeiraAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/eventos")]
    public async Task<IActionResult> ProxyEventos([FromQuery] int hours = 48)
    {
        var result = await diagnosticoService.FetchEventosAsync(hours);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/slo")]
    public async Task<IActionResult> ProxySlo([FromQuery] int hours = 24)
    {
        var result = await diagnosticoService.FetchSloAsync(hours);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [HttpPost]
    [Route("diagnostico/api/alertas/{alertaId}/ack")]
    public async Task<IActionResult> ProxyAckAlerta(string alertaId, [FromBody] object body)
    {
        var result = await diagnosticoService.AckAlertaAsync(alertaId, body);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/alertas/acks")]
    public async Task<IActionResult> ProxyGetAcks([FromQuery] string? ids = null)
    {
        var result = await diagnosticoService.FetchAcksAsync(ids);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/queries-lentas")]
    public async Task<IActionResult> ProxyQueriesLentas()
    {
        var result = await diagnosticoService.FetchQueriesLentasAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [AllowAnonymous]
    [Route("diagnostico/api/health/empresas")]
    public async Task<IActionResult> ProxyHealthEmpresas()
    {
        var result = await diagnosticoService.FetchHealthEmpresasAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }
}
