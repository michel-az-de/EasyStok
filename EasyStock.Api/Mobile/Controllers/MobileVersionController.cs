using System.Reflection;
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
[ApiExplorerSettings(GroupName = "mobile-v1")]
public class MobileVersionController(EasyStockDbContext db) : ControllerBase
{
    private readonly EasyStockDbContext _db = db;

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
        // PWA usa pra decidir se precisa pedir ao operador pra atualizar APK.
        const int mobileSchemaVersion = 1;

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

        // Features que o cliente pode usar condicionalmente (UI + flows).
        // Anonymous=true significa que /sync ainda aceita sem header.
        // Quando Onda 1 entrar, vira false.
        var features = new MobileFeatures(
            ApiKeyEnforced: false,
            Pairing: false,
            ErrorReporting: true,
            Diagnostics: true
        );

        return Ok(new MobileVersionResponse(
            ApiVersion: asmVer,
            MobileSchemaVersion: mobileSchemaVersion,
            ServerTime: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Status: dbOk ? "ok" : "degraded",
            DatabaseOk: dbOk,
            SupportedMutations: supportedMutations,
            Features: features
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
    MobileFeatures Features
);

/// <summary>Capabilities do servidor que o PWA usa pra UI condicional.</summary>
public record MobileFeatures(
    bool ApiKeyEnforced,
    bool Pairing,
    bool ErrorReporting,
    bool Diagnostics
);
