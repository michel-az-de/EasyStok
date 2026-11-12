using System.Text.Json;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.RateLimiting;

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
    [RequestSizeLimit(200_000)]
    public async Task<IActionResult> UploadTrace([FromBody] TraceUploadRequest req, CancellationToken ct)
    {
        if (req is null) return BadRequest(new { error = "payload obrigatório" });

        // Limita tamanho do trace pra evitar abuse (200 entries x ~500B = 100KB max).
        var traceJson = req.Trace.ValueKind == JsonValueKind.Array
            ? req.Trace.GetRawText()
            : "[]";
        if (traceJson.Length > 150_000)
        {
            log.LogWarning("MobileDiagTrace: trace > 150KB do device {DeviceId} truncado", ParaLog(req.DeviceId, 100));
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

        // #2367: endpoint anônimo. O log não leva empresa/loja e a resposta não diz se o aparelho existe,
        // para não servir de oráculo. Campos livres perdem quebra de linha para não forjar linhas de log.
        log.LogInformation(
            "MobileDiagTrace recebido | device={DeviceId} conhecido={Conhecido} apiBaseUrl={ApiBaseUrl} hasConfig={HasConfig} hasProvisioning={HasProvisioning} hasPairing={HasPairing} ua={UserAgent} notes={Notes} trace={Trace}",
            ParaLog(req.DeviceId, 100),
            device != null,
            ParaLog(req.ApiBaseUrl, 200),
            req.HasConfig?.ToString() ?? "?",
            req.HasProvisioning?.ToString() ?? "?",
            req.HasPairing?.ToString() ?? "?",
            ParaLog(req.UserAgent, 200),
            ParaLog(req.Notes, 2_000),
            traceJson);

        return Ok(new { received = true });
    }

    /// <summary>Prepara texto vindo do cliente para o log: troca CR/LF por espaço e corta em <paramref name="max"/>.</summary>
    public static string ParaLog(string? valor, int max)
    {
        if (string.IsNullOrEmpty(valor)) return "";
        var limpo = valor.Replace('\r', ' ').Replace('\n', ' ');
        return limpo.Length > max ? limpo[..max] : limpo;
    }
}
