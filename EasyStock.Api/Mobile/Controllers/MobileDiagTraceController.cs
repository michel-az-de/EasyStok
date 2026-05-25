using System.Text.Json;
using EasyStock.Api.Mobile.Security;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Recebe trace de sync.js de devices que ainda nao conseguiram parear OU
/// querem reportar problema sem depender de auth. Endpoint anonimo, rate-limited.
///
/// Uso tipico: APK pre-configurado mas auto-pair nao funcionou (CORS, deploy
/// errado, secret divergente). Operador toca "Copiar trace" no Diagnostico
/// e cola via formulario externo OU o proprio app POSTa aqui.
///
/// O trace e' so logado via Serilog — visivel no painel Render/Fly. Server
/// nao persiste em DB (volume baixo, conteudo curto, e o trace local ja
/// sobrevive em localStorage do mobile pra retry posterior).
/// </summary>
[ApiController]
[Route("api/mobile/diag")]
[AllowAnonymous]
public class MobileDiagTraceController(
    EasyStockDbContext db,
    ILogger<MobileDiagTraceController> log) : ControllerBase
{
    public sealed record TraceUploadRequest(
        string DeviceId,
        string? ApiBaseUrl,
        bool? HasConfig,
        bool? HasProvisioning,
        bool? HasPairing,
        string? UserAgent,
        JsonElement Trace,           // array de entries do cdb-sync-trace
        string? Notes
    );

    [HttpPost("trace")]
    [EnableRateLimiting("mobile-anonymous")]
    public async Task<IActionResult> UploadTrace([FromBody] TraceUploadRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { error = "payload obrigatório" });

        // Limita tamanho do trace pra evitar abuse (200 entries x ~500B = 100KB max).
        var traceJson = req.Trace.ValueKind == JsonValueKind.Array
            ? req.Trace.GetRawText()
            : "[]";
        if (traceJson.Length > 150_000)
        {
            log.LogWarning("MobileDiagTrace: trace > 150KB do device {DeviceId} truncado", req.DeviceId);
            traceJson = traceJson.Substring(0, 150_000) + "...[truncado]";
        }

        // Tenta vincular ao device pareado (se existir) so pra contexto. Sem
        // throw se nao achar — endpoint anonimo intencionalmente nao exige auth.
        MobileDevice? device = null;
        if (!string.IsNullOrWhiteSpace(req.DeviceId))
        {
            device = await db.Set<MobileDevice>().AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == req.DeviceId, ct);
        }

        log.LogWarning(
            "MobileDiagTrace recebido | device={DeviceId} apiBaseUrl={ApiBaseUrl} hasConfig={HasConfig} hasProvisioning={HasProvisioning} hasPairing={HasPairing} empresaId={EmpresaId} lojaId={LojaId} ua={UserAgent}\nNotes: {Notes}\nTrace:\n{Trace}",
            req.DeviceId,
            req.ApiBaseUrl ?? "(none)",
            req.HasConfig?.ToString() ?? "?",
            req.HasProvisioning?.ToString() ?? "?",
            req.HasPairing?.ToString() ?? "?",
            device?.EmpresaId.ToString() ?? "(sem device)",
            device?.LojaId.ToString() ?? "(sem device)",
            (req.UserAgent ?? "").Length > 200 ? req.UserAgent!.Substring(0, 200) : req.UserAgent ?? "",
            req.Notes ?? "(nenhuma)",
            traceJson);

        return Ok(new { received = true, deviceKnown = device != null });
    }
}
