using System.Reflection;
using EasyStock.Api.Mobile.Services;
using EasyStock.Domain.Entities.Mobile;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Health/version endpoint do módulo Mobile.
///
/// O PWA chama isso no boot pra detectar:
///   - se o servidor está vivo
///   - qual versão do contrato de mutations o servidor entende
///   - se a auth via X-Mobile-Api-Key está ativa
///   - quais features o servidor suporta (pra UI condicional)
///
/// Anônimo de propósito — não precisa estar pareado pra fazer ping.
/// </summary>
[ApiController]
[Route("api/mobile/version")]
[AllowAnonymous]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("mobile-anonymous")]
public class MobileVersionController(
    EasyStockDbContext db,
    IConfiguration configuration,
    IPwaVersionProvider pwaVersion) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;
    private readonly IConfiguration _configuration = configuration;
    private readonly IPwaVersionProvider _pwaVersion = pwaVersion;

    /// <summary>
    /// Retorna versão do contrato Mobile, hora do servidor e capabilities.
    /// Latência baixa: faz uma única consulta de COUNT pra confirmar DB ok.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MobileVersionResponse>> Get(CancellationToken ct)
    {
        // Versão do bundle do API (assembly informational version)
        var asmVer = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";

        // Tenta confirmar DB respondendo (CountAsync é leve em PG)
        var dbOk = false;
        try
        {
            _ = await _db.Set<Product>().AsNoTracking().CountAsync(ct);
            dbOk = true;
        }
        catch
        {
            dbOk = false;
        }

        // mobileSchemaVersion: incrementar quando contrato de mutations mudar.
        // v1 = baseline. v2 = Onda 1 (multi-tenant + auth). PWA pode usar pra
        // decidir se UI mostra "atualize o APK" quando server avançar contrato.
        const int mobileSchemaVersion = 2;

        // Tipos de mutation que o servidor sabe processar HOJE.
        // Quando expandirmos pra cashClosing/auditLog (Onda 3+), incluímos aqui.
        var supportedMutations = new[]
        {
            "product.upsert",
            "client.upsert",
            "order.upsert",
            "batch.upsert",
            "cashEntry.upsert",
        };

        // Comandos remotos que o servidor sabe enfileirar.
        // PWA usa pra decidir se UI da web pode oferecer um botão (ex: pwa_update).
        // Sincronizado com DevicePairingController.AllowedCommandTypes.
        var supportedCommands = new[]
        {
            "flush_now", "pull_now", "reload", "message",
            "pwa_update", "clear_cache",
        };

        // Onda 1 entregue: pareamento de devices + ApiKey middleware.
        // ApiKeyEnforced reflete a flag Mobile:RequireApiKey:
        //   false (default) = transição. /sync aceita anônimo OU pareado.
        //   true            = produção. /sync exige X-Mobile-Api-Key.
        var requireApiKey = _configuration.GetValue<bool>("Mobile:RequireApiKey", false);
        var features = new MobileFeatures(
            ApiKeyEnforced: requireApiKey,
            Pairing: true,
            ErrorReporting: true,
            Diagnostics: true
        );

        // OTA / atualização do app (PWA + APK).
        // PwaCacheVersionCanary vem do IPwaVersionProvider, que lê o CACHE_VERSION
        // diretamente do wwwroot/pwa/sw.js — fonte da verdade do bundle servido.
        // PwaCacheVersionStable vem de Mobile:Pwa:StableCacheVersion em
        // appsettings — snapshot conhecido como bom. Admin promove canary→stable
        // alterando essa config quando confiar na versão.
        // PwaCacheVersion (campo legado) resolve pra Canary ou Stable conforme o
        // device tem flag IsCanary. Devices anônimos (sem X-Device-Id) sempre
        // recebem Stable, pra Casa da Babá inteira ficar em stable por default.
        //
        // Kill switch: Ota:Enabled=false congela TUDO — devices recebem
        // PwaCacheVersion="" e nao atualizam. Util quando estamos testando.
        var otaEnabled = _configuration.GetValue<bool>("Ota:Enabled", true);
        var canaryCacheVersion = _pwaVersion.GetCurrentCacheVersion();
        var stableCacheVersion = _configuration["Mobile:Pwa:StableCacheVersion"]
            ?? canaryCacheVersion; // sem stable configurado → atual vira stable

        var isCanaryDevice = false;
        if (HttpContext.Request.Headers.TryGetValue("X-Device-Id", out var deviceIdHeader))
        {
            var deviceId = deviceIdHeader.ToString();
            if (!string.IsNullOrWhiteSpace(deviceId) && deviceId.Length <= 64)
            {
                isCanaryDevice = await _db.Set<MobileDevice>()
                    .AsNoTracking()
                    .Where(d => d.Id == deviceId && !d.Revoked)
                    .Select(d => d.IsCanary)
                    .FirstOrDefaultAsync(ct);
            }
        }

        var resolvedPwaVersion = !otaEnabled
            ? string.Empty
            : (isCanaryDevice ? canaryCacheVersion : stableCacheVersion);

        var ota = new MobileOtaInfo(
            PwaCacheVersion: resolvedPwaVersion,
            PwaCacheVersionStable: stableCacheVersion,
            PwaCacheVersionCanary: canaryCacheVersion,
            ApkVersion: _configuration["Mobile:Apk:Version"] ?? "0.0.0",
            ApkUrl: _configuration["Mobile:Apk:Url"] ?? "",
            ApkSha256: _configuration["Mobile:Apk:Sha256"] ?? "",
            MinSupportedSchemaVersion: _configuration.GetValue<int>("Mobile:MinSupportedSchemaVersion", 1),
            OtaEnabled: otaEnabled,
            IsCanaryDevice: isCanaryDevice
        );

        return Ok(new MobileVersionResponse(
            ApiVersion: asmVer,
            MobileSchemaVersion: mobileSchemaVersion,
            ServerTime: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status: dbOk ? "ok" : "degraded",
            DatabaseOk: dbOk,
            SupportedMutations: supportedMutations,
            SupportedCommands: supportedCommands,
            Features: features,
            Ota: ota
        ));
    }
}

/// <summary>Resposta do endpoint <c>GET /api/mobile/version</c>.</summary>
public record MobileVersionResponse(
    string ApiVersion,
    int MobileSchemaVersion,
    long ServerTime,
    string Status,
    bool DatabaseOk,
    string[] SupportedMutations,
    string[] SupportedCommands,
    MobileFeatures Features,
    MobileOtaInfo Ota
);

/// <summary>Metadados de atualização (OTA) — usados pelo PWA e pelo APK.</summary>
public record MobileOtaInfo(
    string PwaCacheVersion,
    string PwaCacheVersionStable,
    string PwaCacheVersionCanary,
    string ApkVersion,
    string ApkUrl,
    string ApkSha256,
    int MinSupportedSchemaVersion,
    bool OtaEnabled,
    bool IsCanaryDevice
);

/// <summary>Capabilities do servidor que o PWA usa pra UI condicional.</summary>
public record MobileFeatures(
    bool ApiKeyEnforced,
    bool Pairing,
    bool ErrorReporting,
    bool Diagnostics
);
