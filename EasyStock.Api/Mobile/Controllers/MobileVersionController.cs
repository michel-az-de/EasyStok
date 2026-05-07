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
        // PwaCacheVersion vem do IPwaVersionProvider, que lê o CACHE_VERSION
        // diretamente do wwwroot/pwa/sw.js — fonte da verdade do bundle servido.
        // PWA compara o valor recebido com o cdb-pwa-installed-version local; se
        // diferir, dispara update do SW + reload (ver sync.js > checkPwaUpdate).
        // ApkUrl/ApkVersion/ApkSha256: APK lê esses campos no boot e oferece
        // download da nova build quando ApkVersion > versão local. URL aponta
        // pro Azure Blob Storage do bucket easystock-apk.
        var ota = new MobileOtaInfo(
            PwaCacheVersion: _pwaVersion.GetCurrentCacheVersion(),
            ApkVersion: _configuration["Mobile:Apk:Version"] ?? "0.0.0",
            ApkUrl: _configuration["Mobile:Apk:Url"] ?? "",
            ApkSha256: _configuration["Mobile:Apk:Sha256"] ?? "",
            MinSupportedSchemaVersion: _configuration.GetValue<int>("Mobile:MinSupportedSchemaVersion", 1)
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
    string ApkVersion,
    string ApkUrl,
    string ApkSha256,
    int MinSupportedSchemaVersion
);

/// <summary>Capabilities do servidor que o PWA usa pra UI condicional.</summary>
public record MobileFeatures(
    bool ApiKeyEnforced,
    bool Pairing,
    bool ErrorReporting,
    bool Diagnostics
);
