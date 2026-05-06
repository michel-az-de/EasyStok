using EasyStock.Api.Data;
using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Infra.Postgre.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoint operacional pra disparar o seed de dados de demonstração sob demanda.
/// Útil quando o admin precisa popular o banco com tenants/lojas/usuários de teste
/// sem precisar reiniciar a API. Bloqueado em produção via flag explícita
/// <c>SEED_API_ENABLED=true</c> — reduz pegada de risco em ambientes reais.
/// </summary>
[ApiController]
[Route("api/admin/seed")]
[Authorize(Policy = "SuperAdmin")]
public class AdminSeedController(
    IServiceProvider services,
    EasyStockDbContext db,
    AdminAuditService audit,
    IConfiguration config,
    ILogger<AdminSeedController> logger) : EasyStockControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var enabled = SeedApiEnabled();
        var counts = new
        {
            empresas = await db.Empresas.AsNoTracking().CountAsync(),
            usuarios = await db.Usuarios.AsNoTracking().CountAsync(),
            lojas = await db.Lojas.AsNoTracking().CountAsync(),
            planos = await db.Planos.AsNoTracking().CountAsync()
        };
        return DataOk(new { enabled, counts });
    }

    /// <summary>
    /// Executa o seed completo (multi-tenant). Por padrão usa volume Large (4 tenants:
    /// PastaBella, Cantina Mauricio, CasaDaBaba, MassasVeneza). Volume opcional via
    /// querystring <c>?volume=small|medium|large</c>.
    /// </summary>
    [HttpPost("demo")]
    public async Task<IActionResult> ExecutarDemo([FromQuery] string? volume = null)
    {
        if (!SeedApiEnabled())
            return DataBadRequest("Seed via API não está habilitado neste ambiente. Defina SEED_API_ENABLED=true ou execute via reinicialização da API.");

        // Backup do volume original pra restaurar depois (não vazar configuração entre chamadas).
        var originalVolume = Environment.GetEnvironmentVariable("SEED_DEMO_VOLUME");
        var originalDemo = Environment.GetEnvironmentVariable("SEED_DEMO_DATA");
        try
        {
            if (!string.IsNullOrWhiteSpace(volume))
                Environment.SetEnvironmentVariable("SEED_DEMO_VOLUME", volume);
            // Garante que a flag de "minimal" não interfere.
            Environment.SetEnvironmentVariable("SEED_DEMO_DATA", "true");

            // Cria scope dedicado — SeedData.ExecutarAsync espera IServiceProvider de scope.
            using var scope = services.CreateScope();
            await SeedData.ExecutarAsync(scope.ServiceProvider, logger);

            await audit.LogAsync("SeedDemoExecutado", $"Volume={volume ?? "default"}");

            var counts = new
            {
                empresas = await db.Empresas.AsNoTracking().CountAsync(),
                usuarios = await db.Usuarios.AsNoTracking().CountAsync(),
                lojas = await db.Lojas.AsNoTracking().CountAsync()
            };
            return DataOk(new
            {
                ok = true,
                volume = volume ?? "large",
                mensagem = "Seed executado. Atualize a tela de Clientes pra ver os tenants.",
                counts
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao executar seed demo");
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao executar seed.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SEED_DEMO_VOLUME", originalVolume);
            Environment.SetEnvironmentVariable("SEED_DEMO_DATA", originalDemo);
        }
    }

    /// <summary>
    /// Seed mínimo: apenas 1 admin + 1 empresa + 1 loja. Não inclui dados de demonstração.
    /// </summary>
    [HttpPost("minimal")]
    public async Task<IActionResult> ExecutarMinimal()
    {
        if (!SeedApiEnabled())
            return DataBadRequest("Seed via API não está habilitado. Defina SEED_API_ENABLED=true.");

        try
        {
            using var scope = services.CreateScope();
            await SeedData.ExecutarMinimalAsync(scope.ServiceProvider, logger);
            await audit.LogAsync("SeedMinimalExecutado");
            return DataOk(new { ok = true, mensagem = "Seed mínimo executado." });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha ao executar seed minimal");
            return Problem(detail: ex.Message, statusCode: 500, title: "Erro ao executar seed.");
        }
    }

    private bool SeedApiEnabled()
    {
        // Aceita config (appsettings) OU env var. Default: false em prod, true em dev.
        var fromEnv = Environment.GetEnvironmentVariable("SEED_API_ENABLED");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return string.Equals(fromEnv, "true", StringComparison.OrdinalIgnoreCase);
        var fromConfig = config.GetValue<bool?>("Seed:ApiEnabled");
        return fromConfig ?? false;
    }
}
