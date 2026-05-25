using System.Diagnostics;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// Diagnóstico da infra para operadores e admins. Todos os endpoints /diagnostico/api/*
/// são proxies para a API principal — mantém a sessão cookie do Web e injeta o Bearer
/// via <see cref="DiagnosticoWebService"/>.
/// </summary>
public class DiagnosticoController(DiagnosticoWebService diagnosticoService) : Controller
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet]
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

        ViewBag.IsAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
        ViewBag.ActiveMenuItem = "Diagnostico";

        return View(apiResult);
    }

    [Authorize]
    [HttpGet]
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

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/endpoints")]
    public async Task<IActionResult> ProxyEndpoints()
    {
        var result = await diagnosticoService.FetchEndpointTestsAsync();
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/historico")]
    public async Task<IActionResult> ProxyHistorico()
    {
        var result = await diagnosticoService.FetchHealthHistoryAsync();
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/logs-enhanced")]
    public async Task<IActionResult> ProxyEnhancedLogs([FromQuery] int hours = 48)
    {
        var result = await diagnosticoService.FetchEnhancedLogsAsync(null, hours);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível obter logs da API" });
        return base.Json(result);
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [IgnoreAntiforgeryToken]
    [HttpPost]
    [Route("diagnostico/api/logs/limpar")]
    public async Task<IActionResult> ProxyLimparLogs()
    {
        var result = await diagnosticoService.LimparLogsAsync(null);
        if (result is null)
            return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet]
    [Route("diagnostico/api/logs/exportar")]
    public async Task<IActionResult> ProxyExportarLogs([FromQuery] int hours = 48)
    {
        var (stream, fileName) = await diagnosticoService.ExportarLogsAsync(null, hours);
        if (stream is null)
            return StatusCode(502, new { error = "Não foi possível obter logs da API" });

        return File(stream, "text/plain; charset=utf-8", fileName ?? $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.log");
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [IgnoreAntiforgeryToken]
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

    [Authorize(Roles = "Admin,SuperAdmin")]
    [HttpGet]
    [Route("diagnostico/api/logs/lixeira")]
    public async Task<IActionResult> ProxyLixeira()
    {
        var result = await diagnosticoService.FetchLixeiraAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize(Roles = "Admin,SuperAdmin")]
    [IgnoreAntiforgeryToken]
    [HttpPost]
    [Route("diagnostico/api/logs/lixeira/esvaziar")]
    public async Task<IActionResult> ProxyEsvaziarLixeira()
    {
        var result = await diagnosticoService.EsvaziarLixeiraAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/eventos")]
    public async Task<IActionResult> ProxyEventos([FromQuery] int hours = 48)
    {
        var result = await diagnosticoService.FetchEventosAsync(hours);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/slo")]
    public async Task<IActionResult> ProxySlo([FromQuery] int hours = 24)
    {
        var result = await diagnosticoService.FetchSloAsync(hours);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost]
    [Route("diagnostico/api/alertas/{alertaId}/ack")]
    public async Task<IActionResult> ProxyAckAlerta(string alertaId, [FromBody] object body)
    {
        var result = await diagnosticoService.AckAlertaAsync(alertaId, body);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/alertas/acks")]
    public async Task<IActionResult> ProxyGetAcks([FromQuery] string? ids = null)
    {
        var result = await diagnosticoService.FetchAcksAsync(ids);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost]
    [Route("diagnostico/api/alertas/acks")]
    public async Task<IActionResult> ProxyPostAcks([FromBody] AcksBatchRequest request)
    {
        if (request?.Ids is null || request.Ids.Count == 0)
            return Json(new { acks = Array.Empty<object>() });

        // Deduplicar e limitar server-side (defesa em profundidade)
        var ids = request.Ids.Distinct().Take(200).ToList();
        var csv = string.Join(",", ids);
        var result = await diagnosticoService.FetchAcksAsync(csv);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/queries-lentas")]
    public async Task<IActionResult> ProxyQueriesLentas()
    {
        var result = await diagnosticoService.FetchQueriesLentasAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/health/empresas")]
    public async Task<IActionResult> ProxyHealthEmpresas()
    {
        var result = await diagnosticoService.FetchHealthEmpresasAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost]
    [Route("diagnostico/api/historico/zerar")]
    public async Task<IActionResult> ProxyZerarHistorico()
    {
        var result = await diagnosticoService.ZerarHistoricoAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [IgnoreAntiforgeryToken]
    [HttpPost]
    [Route("diagnostico/api/logs/expurgar")]
    public async Task<IActionResult> ProxyExpurgarLogs([FromQuery] int diasManter = 3)
    {
        var result = await diagnosticoService.ExpurgarLogsAsync(diasManter);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/logs/storage")]
    public async Task<IActionResult> ProxyStorageFiles()
    {
        var result = await diagnosticoService.FetchStorageFilesAsync();
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }

    [Authorize]
    [HttpGet]
    [Route("diagnostico/api/logs/storage/conteudo")]
    public async Task<IActionResult> ProxyStorageFileContent([FromQuery] string file)
    {
        if (string.IsNullOrWhiteSpace(file))
            return BadRequest(new { error = "Parâmetro 'file' é obrigatório." });
        var result = await diagnosticoService.FetchStorageFileContentAsync(file);
        if (result is null) return StatusCode(502, new { error = "Não foi possível conectar à API" });
        return base.Json(result);
    }
}

public class AcksBatchRequest
{
    public List<string> Ids { get; set; } = new();
}
