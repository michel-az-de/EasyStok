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
        var (envSet, configSet) = SeedApiEnabledSources();
        var counts = new
        {
            empresas = await db.Empresas.AsNoTracking().CountAsync(),
            usuarios = await db.Usuarios.AsNoTracking().CountAsync(),
            lojas = await db.Lojas.AsNoTracking().CountAsync(),
            planos = await db.Planos.AsNoTracking().CountAsync()
        };
        // Log de cada consulta — fica no Render/Azure logs pra rastreabilidade.
        logger.LogInformation(
            "[Seed] Status consultado — enabled={Enabled} (envSet={EnvSet}, configSet={ConfigSet}); counts={@Counts}",
            enabled, envSet, configSet, counts);
        return DataOk(new
        {
            enabled,
            counts,
            // Diagnóstico — admin enxerga de onde a flag vem (env var vs appsettings).
            // Útil quando "habilitei mas não funciona": indica qual fonte está winning.
            diagnostico = new
            {
                fonteEnv = envSet,
                fonteConfig = configSet,
                hint = enabled
                    ? "OK — seed liberado."
                    : "Pra liberar: defina SEED_API_ENABLED=true (env var no host) OU \"Seed:ApiEnabled\": true em appsettings, e reinicie a API."
            }
        });
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
    /// Seed enxuto pra testar o módulo de Gestão de Cliente do Admin: 4 tenants
    /// com status variados (2 Ativa, 1 Suspensa, 1 Cancelada), 1-2 lojas cada,
    /// 2-3 usuários cada, 5 tickets cobrindo status × prioridade, 3 notas internas
    /// (incluindo 1 tipo Alerta pra acionar banner) e 5 audit logs recentes.
    /// Bem mais leve que /demo (que cria empresas com produtos/movimentações
    /// completas — útil quando você só quer testar o painel admin).
    /// </summary>
    [HttpPost("admin-test-scenarios")]
    public async Task<IActionResult> ExecutarAdminTestScenarios()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("[Seed] admin-test-scenarios — iniciando…");

        if (!SeedApiEnabled())
        {
            var (envSet, cfgSet) = SeedApiEnabledSources();
            logger.LogWarning("[Seed] admin-test-scenarios BLOQUEADO — flag desabilitada (envSet={EnvSet}, configSet={CfgSet})", envSet, cfgSet);
            return DataBadRequest(
                "Seed via API está desabilitado. Defina SEED_API_ENABLED=true (env var) ou \"Seed:ApiEnabled\": true em appsettings, e reinicie a API. " +
                $"Estado atual: env={envSet}, config={cfgSet}.");
        }

        try
        {
            using var scope = services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
            var agora = new DateTime(
                DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day,
                DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0, DateTimeKind.Utc);

            await EasyStock.Api.Data.Tenants.AdminTestScenariosSeed.ExecutarAsync(ctx, agora, logger);

            await audit.LogAsync("SeedAdminTestScenariosExecutado",
                "4 tenants + 5 tickets + 3 notas + 5 audit logs");

            var counts = new
            {
                empresas = await db.Empresas.AsNoTracking().CountAsync(),
                usuarios = await db.Usuarios.AsNoTracking().CountAsync(),
                lojas = await db.Lojas.AsNoTracking().CountAsync(),
                tickets = await db.AdminTickets.AsNoTracking().CountAsync()
            };

            sw.Stop();
            logger.LogInformation(
                "[Seed] admin-test-scenarios — OK em {Ms}ms. counts={@Counts}",
                sw.ElapsedMilliseconds, counts);

            return DataOk(new
            {
                ok = true,
                cenario = "admin-test-scenarios",
                tempoMs = sw.ElapsedMilliseconds,
                counts,
                credenciais = new
                {
                    senhaPadrao = EasyStock.Api.Data.Tenants.AdminTestScenariosSeed.SenhaPadrao,
                    exemplos = new[]
                    {
                        "maria.carvalho@bistro-vila.test (Bistrô — Admin)",
                        "ricardo@padaria-bairro.test (Padaria — Admin, trial)",
                        "bruno.costa@cafe-quasela.test (Café — Suspensa)",
                        "ze.pereira@lojadoze.test (Loja Do Zé — Cancelada)"
                    }
                },
                mensagem = $"Cenários criados em {sw.ElapsedMilliseconds}ms. Atualize Clientes/Dashboard/Tickets pra ver."
            });
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Seed] admin-test-scenarios FALHOU em {Ms}ms — {Msg}", sw.ElapsedMilliseconds, ex.Message);
            return Problem(detail: ex.Message + (ex.InnerException is { } inner ? " | inner: " + inner.Message : ""),
                statusCode: 500, title: "Erro ao executar seed (admin-test-scenarios).");
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

    /// <summary>
    /// Diagnóstico — retorna o estado das duas fontes (env var + appsettings) pro
    /// status mostrar pra o usuário "qual flag tá vencendo". Útil quando "habilitei
    /// mas não funciona" — ajuda a ver qual fonte tem precedência.
    /// </summary>
    private (string Env, string Config) SeedApiEnabledSources()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SEED_API_ENABLED");
        var envState = string.IsNullOrWhiteSpace(fromEnv) ? "(vazio)" : fromEnv;
        var fromConfig = config.GetValue<bool?>("Seed:ApiEnabled");
        var cfgState = fromConfig.HasValue ? fromConfig.Value.ToString().ToLowerInvariant() : "(vazio)";
        return (envState, cfgState);
    }
}
