using System.Security.Claims;
using System.Text.Json;
using EasyStock.Api.Data;
using EasyStock.Api.Http;
using EasyStock.Api.Services;
using EasyStock.Domain.Entities;
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
    IServiceScopeFactory scopeFactory,
    EasyStockDbContext db,
    AdminAuditService audit,
    SeedProgressService seedProgress,
    IConfiguration config,
    IHostEnvironment env,
    ILogger<AdminSeedController> logger) : EasyStockControllerBase
{
    /// <summary>
    /// R6: registro de auditoria forte sempre que algum endpoint de seed e chamado em Production.
    /// Persiste no SystemErrorLog via interceptor de logging e dispara LogCritical (Sentry/alerting).
    /// </summary>
    private void AuditarChamadaEmProducao(string endpoint, object? payload = null)
    {
        if (!env.IsProduction()) return;

        var callerEmail = User.FindFirstValue(ClaimTypes.Email)
                       ?? User.FindFirstValue("email")
                       ?? "(sem email no JWT)";
        var callerUserId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "(sem sub)";
        var sourceIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(sem ip)";

        logger.LogCritical(
            "[Seed][PROD] Endpoint {Endpoint} chamado em Production — caller={CallerEmail} (UserId={CallerUserId}, ip={SourceIp}), payload={@Payload}, ts={Ts}",
            endpoint, callerEmail, callerUserId, sourceIp, payload, DateTime.UtcNow);
    }

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
        AuditarChamadaEmProducao("POST /api/admin/seed/demo", new { volume });
        if (!SeedApiEnabled())
            return DataBadRequest(SeedDisabledMessage());

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
            using var scope = scopeFactory.CreateScope();
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

        AuditarChamadaEmProducao("POST /api/admin/seed/admin-test-scenarios");
        if (!SeedApiEnabled())
        {
            var (envSet, cfgSet) = SeedApiEnabledSources();
            logger.LogWarning("[Seed] admin-test-scenarios BLOQUEADO — flag desabilitada (envSet={EnvSet}, configSet={CfgSet})", envSet, cfgSet);
            return DataBadRequest(
                SeedDisabledMessage() + $" Estado atual: env={envSet}, config={cfgSet}.");
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
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
        AuditarChamadaEmProducao("POST /api/admin/seed/minimal");
        if (!SeedApiEnabled())
            return DataBadRequest(SeedDisabledMessage());

        try
        {
            using var scope = scopeFactory.CreateScope();
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

    // ─────────────── Async run endpoints (UI com progresso em tempo real) ───────

    /// <summary>
    /// Dispara o seed em background e retorna imediatamente com runId.
    /// UI faz polling em <see cref="GetRunStatus"/> a cada 500ms enquanto Status=Running.
    /// </summary>
    /// <param name="tipo">adminTestScenarios | demo | minimal</param>
    /// <param name="volume">small|medium|large (só pra tipo=demo)</param>
    [HttpPost("run-async")]
    public IActionResult RunAsync([FromQuery] string tipo = "adminTestScenarios", [FromQuery] string? volume = null)
    {
        AuditarChamadaEmProducao("POST /api/admin/seed/run-async", new { tipo, volume });
        if (!SeedApiEnabled())
        {
            var (envSet, cfgSet) = SeedApiEnabledSources();
            return DataBadRequest(
                SeedDisabledMessage() + $" (env={envSet}, config={cfgSet})");
        }

        var adminEmail = User.FindFirstValue(ClaimTypes.Email)
                        ?? User.FindFirstValue("email")
                        ?? "unknown";

        var runId = Guid.NewGuid();
        seedProgress.Create(runId, adminEmail, tipo, volume);

        // Dispara em background — não bloqueia o request.
        _ = Task.Run(() => ExecutarSeedBackgroundAsync(runId, tipo, volume, adminEmail));

        logger.LogInformation("[Seed] run-async iniciado — RunId={RunId}, Tipo={Tipo}, Volume={Volume}, Admin={Admin}",
            runId, tipo, volume, adminEmail);

        return DataOk(new
        {
            runId,
            tipo,
            volume,
            mensagem = $"Seed '{tipo}' iniciado. Acompanhe o progresso via GET /api/admin/seed/run/{runId}."
        });
    }

    /// <summary>Polling endpoint — retorna estado atual do run (100ms-friendly).</summary>
    [HttpGet("run/{runId:guid}")]
    public IActionResult GetRunStatus(Guid runId)
    {
        var state = seedProgress.Get(runId);
        if (state is null)
        {
            // Pode estar no DB (run antigo). Tenta buscar.
            var log = db.SeedRunLogs.AsNoTracking().FirstOrDefault(x => x.Id == runId);
            if (log is null) return NotFound(new { error = $"Run {runId} não encontrado." });

            List<object>? etapas = null;
            if (!string.IsNullOrEmpty(log.EtapasJson))
            {
                try { etapas = JsonSerializer.Deserialize<List<object>>(log.EtapasJson); } catch { }
            }
            return DataOk(new
            {
                runId,
                status = log.Status,
                percent = log.Status == "Success" ? 100 : 0,
                currentStep = log.Resumo ?? log.Erro ?? log.Status,
                etapas,
                erro = log.Erro,
                resumo = log.Resumo,
                startedAt = log.StartedAt,
                completedAt = log.CompletedAt
            });
        }

        // EtapasSnapshot retorna cópia imutável — evita race com background task
        // que está adicionando etapas em paralelo (List<T>.Add + iteração concorrente
        // pode corromper o array interno).
        return DataOk(new
        {
            runId = state.RunId,
            status = state.Status,
            percent = state.Percent,
            currentStep = state.CurrentStep,
            etapas = state.EtapasSnapshot().Select(e => new { e.Ts, e.Level, e.Mensagem, e.Percent }),
            erro = state.Erro,
            resumo = state.Resumo,
            startedAt = state.StartedAt,
            completedAt = state.CompletedAt
        });
    }

    /// <summary>Histórico dos últimos runs (auditoria — quem rodou, quando, resultado).</summary>
    [HttpGet("runs")]
    public async Task<IActionResult> GetRuns([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page = Math.Max(1, page);
        var total = await db.SeedRunLogs.CountAsync();
        var items = await db.SeedRunLogs
            .AsNoTracking()
            .OrderByDescending(x => x.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.AdminEmail,
                x.TipoSeed,
                x.Volume,
                x.StartedAt,
                x.CompletedAt,
                x.Status,
                x.Resumo,
                x.Erro
            })
            .ToListAsync();
        return DataOk(new { items, total, page, pageSize });
    }

    /// <summary>
    /// Timeout máximo de execução de qualquer seed em background. Sem isso,
    /// um run travado fica como Status=Running pra sempre até ser despejado
    /// pelo cap de 50 do SeedProgressService.
    /// </summary>
    private static readonly TimeSpan SeedExecutionTimeout = TimeSpan.FromMinutes(5);

    private async Task ExecutarSeedBackgroundAsync(Guid runId, string tipo, string? volume, string adminEmail)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        using var cts = new CancellationTokenSource(SeedExecutionTimeout);
        var ct = cts.Token;

        // CRÍTICO: scope NOVO criado via IServiceScopeFactory (singleton).
        // IServiceProvider request-scoped seria descartado quando o controller
        // retorna 200 com o runId — Task.Run roda DEPOIS, e `services.CreateScope()`
        // dispararia "Cannot access a disposed object: IServiceProvider".
        using var scope = scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var ctx = sp.GetRequiredService<EasyStockDbContext>();
        // audit também é scoped — resolvido do scope novo (não do controller).
        var scopedAudit = sp.GetRequiredService<AdminAuditService>();

        // Flag pra reportar honestamente: rolledBack=true só se de fato houve
        // tx ativa quando o erro ocorreu. Antes mentíamos pra demo/minimal.
        bool transactionActive = false;

        try
        {
            ct.ThrowIfCancellationRequested();

            // ── Schema bootstrap defensivo ───────────────────────────────────────
            // Não depende de migrations EF terem rodado certinho — garante
            // IsSeedData + SeedRunLogs via SQL idempotente. Se ambiente já tem,
            // é no-op. Se faltava algo (deploy parcial, migration vazia, etc),
            // agora está lá. Sem isso, seed quebra com "column does not exist".
            seedProgress.Report(runId, 1, "Verificando schema do banco…");
            await Data.SeedSchemaBootstrap.EnsureAsync(ctx, logger, ct);
            seedProgress.Report(runId, 3, "Schema OK — iniciando seed.");

            var agora = new DateTime(
                DateTime.UtcNow.Year, DateTime.UtcNow.Month, DateTime.UtcNow.Day,
                DateTime.UtcNow.Hour, DateTime.UtcNow.Minute, 0, DateTimeKind.Utc);

            switch (tipo.ToLowerInvariant())
            {
                case "admintestscenarios":
                    // ExecutarAsync já gerencia sua própria tx + chama SuccessAsync por dentro.
                    transactionActive = true;
                    await EasyStock.Api.Data.Tenants.AdminTestScenariosSeed.ExecutarAsync(
                        ctx, agora, logger, seedProgress, runId);
                    break;

                case "demo":
                    {
                        // SeedData.ExecutarAsync gerencia sua PRÓPRIA tx + ExecutionStrategy
                        // internamente (SeedData.cs:39,45). Aninhar 2 ExecutionStrategy quebra
                        // com "does not support user-initiated transactions". Aqui só configura
                        // env vars + chama, transação é responsabilidade do SeedData.
                        var originalVolume = Environment.GetEnvironmentVariable("SEED_DEMO_VOLUME");
                        var originalDemo = Environment.GetEnvironmentVariable("SEED_DEMO_DATA");
                        transactionActive = true; // SeedData abre tx interna
                        try
                        {
                            seedProgress.Report(runId, 10, $"Configurando demo volume={volume ?? "large"}…");
                            if (!string.IsNullOrWhiteSpace(volume))
                                Environment.SetEnvironmentVariable("SEED_DEMO_VOLUME", volume);
                            Environment.SetEnvironmentVariable("SEED_DEMO_DATA", "true");
                            seedProgress.Report(runId, 20, "Iniciando seed demo completo…");
                            await SeedData.ExecutarAsync(scope.ServiceProvider, logger);
                            transactionActive = false; // SeedData committed sua tx com sucesso
                            await seedProgress.SuccessAsync(runId,
                                $"Seed demo (volume={volume ?? "large"}) concluído em {sw.ElapsedMilliseconds}ms.");
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable("SEED_DEMO_VOLUME", originalVolume);
                            Environment.SetEnvironmentVariable("SEED_DEMO_DATA", originalDemo);
                        }
                    }
                    break;

                case "minimal":
                    {
                        // Idem demo — SeedData.ExecutarMinimalAsync já é transacional (SeedData.cs:87,93).
                        transactionActive = true;
                        seedProgress.Report(runId, 20, "Executando seed mínimo (1 admin + 1 empresa + 1 loja)…");
                        await SeedData.ExecutarMinimalAsync(scope.ServiceProvider, logger);
                        transactionActive = false;
                        await seedProgress.SuccessAsync(runId,
                            $"Seed mínimo concluído em {sw.ElapsedMilliseconds}ms.");
                    }
                    break;

                default:
                    await seedProgress.FailureAsync(runId, $"Tipo de seed desconhecido: '{tipo}'.", rolledBack: false);
                    return;
            }

            await scopedAudit.LogAsync("SeedRunAsyncConcluido",
                $"RunId={runId}, Tipo={tipo}, Volume={volume ?? "-"}, Tempo={sw.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            sw.Stop();
            logger.LogError("[Seed] Timeout ({Min}min) — RunId={RunId}, Tipo={Tipo}",
                SeedExecutionTimeout.TotalMinutes, runId, tipo);
            // Se havia tx ativa, EF Core já a marcou pra rollback no dispose do `await using`.
            await seedProgress.FailureAsync(runId,
                $"Timeout — execução excedeu {SeedExecutionTimeout.TotalMinutes} min.",
                rolledBack: transactionActive);
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Seed] ExecutarSeedBackgroundAsync falhou — RunId={RunId}, Tipo={Tipo}", runId, tipo);
            // rolledBack é honesto agora: só true se havia tx ativa quando explodiu.
            // Para casos onde a tx já tinha sido committed (transactionActive=false),
            // o estado do banco ESTÁ alterado e o usuário precisa saber disso.
            await seedProgress.FailureAsync(runId, ex.Message, rolledBack: transactionActive);
        }
    }

    private bool SeedApiEnabled()
    {
        // Aceita config (appsettings) OU env var. Default: false em prod, true em dev.
        var fromEnv = Environment.GetEnvironmentVariable("SEED_API_ENABLED");
        var enabled = !string.IsNullOrWhiteSpace(fromEnv)
            ? string.Equals(fromEnv, "true", StringComparison.OrdinalIgnoreCase)
            : (config.GetValue<bool?>("Seed:ApiEnabled") ?? false);

        if (!enabled) return false;

        // R6: em Production exige DUPLA confirmacao — SEED_API_ENABLED + SEED_API_ALLOW_PROD.
        // Two-keys break-glass: cobrir engano humano de virar a flag normal por curiosidade.
        if (env.IsProduction())
        {
            var allowProd = Environment.GetEnvironmentVariable("SEED_API_ALLOW_PROD");
            if (!string.Equals(allowProd, "true", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogCritical(
                    "[Seed][PROD] Tentativa de uso do endpoint de seed sem SEED_API_ALLOW_PROD=true. SEED_API_ENABLED={Env}",
                    fromEnv ?? "(via config)");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Mensagem padrao quando SeedApiEnabled() retorna false — explica os 2 niveis de opt-in.
    /// </summary>
    private string SeedDisabledMessage()
    {
        if (env.IsProduction())
        {
            return "Seed via API em Production exige SEED_API_ENABLED=true E SEED_API_ALLOW_PROD=true (defesa em duas chaves). " +
                   "Em incidente, ajustar ambas no Azure App Service Settings e reiniciar.";
        }
        return "Seed via API esta desabilitado. Defina SEED_API_ENABLED=true (env var) ou \"Seed:ApiEnabled\": true em appsettings, e reinicie a API.";
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
