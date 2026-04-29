using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Endpoints de diagnóstico e telemetria do PWA Mobile.
/// Recebe payloads que o app coleta localmente (error log, snapshots) e
/// registra via Serilog na infra de logs já existente — sem migration nem
/// tabela nova nesta onda. Quando volume justificar, evolui pra
/// <c>mobile_diagnostic_logs</c> na Onda 1.
/// </summary>
[ApiController]
[Route("api/mobile/diagnostics")]
[AllowAnonymous]
[ApiExplorerSettings(GroupName = "mobile-v1")]
public class MobileDiagnosticsController(ILogger<MobileDiagnosticsController> log) : ControllerBase
{
    private readonly ILogger<MobileDiagnosticsController> _log = log;

    /// <summary>
    /// Recebe lote de erros capturados pelo PWA (window.onerror,
    /// unhandledrejection, scanner, etc). Não persiste em DB ainda — apenas
    /// loga via Serilog com correlationId, deviceId e contexto, pro time
    /// de plantão investigar via dashboard de logs existente.
    /// </summary>
    /// <remarks>
    /// O PWA deve mandar SO opt-in (operador clica "Enviar erros" no
    /// Diagnóstico). Não envia automático pra evitar leak de dados sensíveis
    /// e custos com servidor.
    /// </remarks>
    [HttpPost("errors")]
    public IActionResult ReportErrors([FromBody] MobileErrorReport req)
    {
        if (req == null || req.Errors == null || req.Errors.Length == 0)
            return BadRequest(new { error = "payload vazio" });

        // Cap defensivo: PWA tem ring buffer de 500, mas se algo
        // estranho mandar 50k entradas, corta por seguranca.
        var capped = req.Errors.Length > 500 ? req.Errors[^500..] : req.Errors;

        var deviceId = req.DeviceId ?? "unknown";
        var operatorName = req.OperatorName ?? "anonymous";
        var bundleVersion = req.BundleVersion ?? "unknown";

        _log.LogInformation(
            "[mobile-error-report] device={DeviceId} operator={Operator} bundle={Bundle} count={Count}",
            deviceId, operatorName, bundleVersion, capped.Length);

        foreach (var e in capped)
        {
            // Cada entrada vira um log separado pra search/agrupamento por ctx
            _log.LogWarning(
                "[mobile-error] device={DeviceId} operator={Operator} ctx={Context} screen={Screen} msg={Message} ts={Timestamp}",
                deviceId,
                operatorName,
                e.Context ?? "-",
                e.Screen ?? "-",
                Truncate(e.Message ?? string.Empty, 500),
                e.Timestamp);

            // Stack vai num log debug separado (volumoso) — só aparece se
            // log level estiver baixo no ambiente.
            if (!string.IsNullOrWhiteSpace(e.Stack))
            {
                _log.LogDebug(
                    "[mobile-error-stack] device={DeviceId} ctx={Context}\n{Stack}",
                    deviceId, e.Context ?? "-", Truncate(e.Stack, 4000));
            }
        }

        return Ok(new { received = capped.Length });
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";
}

/// <summary>Payload do report de erros enviado pelo PWA.</summary>
public record MobileErrorReport(
    string? DeviceId,
    string? OperatorName,
    string? BundleVersion,
    MobileErrorEntry[] Errors
);

/// <summary>Uma entrada do <c>cdb-error-log</c> do PWA.</summary>
public record MobileErrorEntry(
    long Timestamp,
    string? Context,
    string? Message,
    string? Stack,
    string? Screen,
    string? Operator
);
