using EasyStock.Api.Data;
using EasyStock.Api.Observability;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace EasyStock.Api.Startup;

/// <summary>
/// Roda migrations pendentes e seeds (SuperAdmin, SeedData demo, NotificacoesGlobais,
/// Mobile schema) no startup, serializado entre réplicas via advisory lock PG.
///
/// Em produção com múltiplas réplicas, desabilitar via <c>RunMigrationsOnStartup=false</c>
/// e rodar migrations em init-container ou job separado antes do deploy. No Render/Cloud
/// Run o entrypoint do container já roda o EF bundle ANTES do app subir — este bloco
/// aqui é rede de segurança e idempotente (no-op se schema já está atualizado).
///
/// Replica que adquirir o advisory lock executa todo o bloco; outras logam skip e seguem
/// boot. Health check <c>/health/ready</c> bloqueia tráfego até a primeira replica concluir.
/// </summary>
public static class StartupMigrationsAndSeed
{
    public static async Task RunAsync(
        WebApplication app,
        ResolvedInfrastructureState infraState,
        string databaseProvider,
        string resolvedProvider)
    {
        // Em produção com múltiplas réplicas, desabilitar via RunMigrationsOnStartup=false
        // e rodar migrations em init-container ou job separado antes do deploy.
        // No Render/Cloud Run o entrypoint do container ja roda o EF bundle ANTES do app
        // subir — esse bloco aqui e' rede de seguranca e idempotente (no-op se schema
        // ja esta atualizado).
        var runMigrationsOnStartup = app.Configuration.GetValue("RunMigrationsOnStartup", defaultValue: !app.Environment.IsProduction());
        var migrationsFailFast = app.Configuration.GetValue("MigrationsFailFast", defaultValue: false);

        app.Logger.LogInformation(
            "[Migrations] Estado lido: Environment={Environment} | Database__Provider={ProviderConfig} | resolvedProvider={Resolved} | RunMigrationsOnStartup={Run} | MigrationsFailFast={FailFast}",
            app.Environment.EnvironmentName, databaseProvider, resolvedProvider, runMigrationsOnStartup, migrationsFailFast);

        if (!runMigrationsOnStartup || resolvedProvider is not "postgresql")
            return;

        // R6: serializa migrations + seeds entre replicas via advisory lock pg_try_advisory_lock.
        // Replica que adquirir o lock executa todo o bloco; outras logam skip e seguem boot.
        // Health check /health/ready bloqueia trafego ate a primeira replica concluir.
        using var lockScope = app.Services.CreateScope();
        var advisoryLock = lockScope.ServiceProvider.GetRequiredService<PostgresAdvisoryLock>();

        var acquired = await advisoryLock.TentarExecutarAsync(LockKeys.StartupMigrationsAndSeed, async lockToken =>
        {
            var migrationsHouveErro = false;
            try
            {
                List<string> appliedMigrations;
                List<string> pendingMigrations;
                using (var checkScope = app.Services.CreateScope())
                {
                    var checkDb = checkScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                    // RLS: queries em __EFMigrationsHistory não dependem da policy
                    // tenant_isolation (tabela não tem EmpresaId), mas a connection
                    // ainda assim entra com tenant=Guid.Empty. Bypass garante que
                    // nada residual de outra request afete a leitura — defesa em
                    // profundidade para o caminho de boot.
                    using var _ = checkDb.UseRowLevelSecurityBypass();
                    appliedMigrations = (await checkDb.Database.GetAppliedMigrationsAsync()).ToList();
                    pendingMigrations = (await checkDb.Database.GetPendingMigrationsAsync()).ToList();
                }

                app.Logger.LogInformation(
                    "[Migrations] {AppliedCount} aplicadas, {PendingCount} pendentes. Pendentes: {Pendentes}",
                    appliedMigrations.Count, pendingMigrations.Count,
                    pendingMigrations.Count == 0 ? "(nenhuma)" : string.Join(", ", pendingMigrations));

                // Migrations conhecidas que historicamente colidem com schema mobile pré-existente
                // (porque criam tabelas que mobile schema raw também cria com IF NOT EXISTS).
                // Para essas, aceitamos 42P07/42701 e registramos manualmente. Para qualquer
                // outra migration, falha real é fail-fast.
                var migrationsComColisaoConhecida = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "20260430193546_AddAdminModule",
                    "20260430210354_RenameAdminAuditLogsTable_AddMissingDbSets"
                };

                foreach (var migrationId in pendingMigrations)
                {
                    var swMigration = System.Diagnostics.Stopwatch.StartNew();
                    app.Logger.LogInformation("[Migrations] >>> Aplicando {MigrationId}...", migrationId);
                    try
                    {
                        using var migScope = app.Services.CreateScope();
                        var migDb = migScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                        // RLS: migrations criam/alteram tabelas tenant-aware — precisam
                        // rodar com bypass, senão a própria migration AddRowLevelSecurity
                        // (e qualquer DML em seed_data interno) fica sob a policy que
                        // ela mesma criou.
                        using var _ = migDb.UseRowLevelSecurityBypass();
                        var migrator = migDb.GetInfrastructure().GetRequiredService<IMigrator>();
                        await migrator.MigrateAsync(migrationId);
                        swMigration.Stop();
                        app.Logger.LogInformation(
                            "[Migrations] <<< {MigrationId} aplicada em {ElapsedMs}ms.",
                            migrationId, swMigration.ElapsedMilliseconds);
                    }
                    catch (Npgsql.PostgresException ex) when (
                        ex.SqlState is "42701" or "42P07" &&
                        migrationsComColisaoConhecida.Contains(migrationId))
                    {
                        swMigration.Stop();
                        app.Logger.LogWarning(
                            "[Migrations] {MigrationId}: schema ja existe ({SqlState}), registrando como aplicada.",
                            migrationId, ex.SqlState);
                        using var regScope = app.Services.CreateScope();
                        var regDb = regScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                        using var _ = regDb.UseRowLevelSecurityBypass();
                        const string productVersion = "9.0.0";
                        await regDb.Database.ExecuteSqlInterpolatedAsync(
                            $"INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES ({migrationId}, {productVersion}) ON CONFLICT DO NOTHING");
                    }
                    catch (Exception ex)
                    {
                        migrationsHouveErro = true;
                        infraState.MigrationsApplied = false;
                        infraState.MigrationError = $"{migrationId}: {ex.GetType().Name}: {ex.Message}";
                        app.Logger.LogError(ex,
                            "[Migrations] !!! FALHA na migration {MigrationId} (SqlState={SqlState}). Stack acima.",
                            migrationId,
                            (ex as Npgsql.PostgresException)?.SqlState ?? "(n/a)");
                        // Continua tentando as proximas pra logar TODAS as falhas. So depois decide se aborta.
                    }
                }

                if (migrationsHouveErro)
                {
                    app.Logger.LogError(
                        "[Migrations] !!! Houve erros aplicando migrations. MigrationsFailFast={FailFast}.",
                        migrationsFailFast);
                    if (migrationsFailFast)
                        throw new InvalidOperationException(
                            "Migrations falharam e MigrationsFailFast=true. Abortando startup. Veja erros acima.");
                }
                else
                {
                    infraState.MigrationsApplied = true;
                    app.Logger.LogInformation(
                        "[Migrations] === Aplicadas com sucesso ({Count} novas). ===",
                        pendingMigrations.Count);
                }
            }
            catch (Exception ex)
            {
                infraState.MigrationsApplied = false;
                infraState.MigrationError ??= ex.Message;
                app.Logger.LogError(ex, "[Migrations] !!! Erro fatal no bloco de migrations.");
                if (migrationsFailFast)
                    throw;
            }

            // Schema bootstrap defensivo: roda DEPOIS de migrations e antes de qualquer
            // seed pra garantir que IsSeedData + SeedRunLogs existam, mesmo se uma
            // migration foi aplicada vazia ou deploy parcial deixou o banco inconsistente.
            // SQL idempotente — no-op se schema já está correto.
            try
            {
                using var bootstrapScope = app.Services.CreateScope();
                var bootstrapDb = bootstrapScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                // RLS: schema bootstrap mexe em tabelas tenant-aware sem JWT contextual.
                using var _ = bootstrapDb.UseRowLevelSecurityBypass();
                await SeedSchemaBootstrap.EnsureAsync(bootstrapDb, app.Logger);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "[SeedSchema] Bootstrap falhou no startup — seed via UI vai tentar de novo no próprio run.");
            }

            // SuperAdmin global ANTES do seed de tenants — o painel /EasyStock.Admin
            // depende dele e nenhum dos seeds de tenant cria SuperAdmin (apenas Admin
            // de empresa). Idempotente: no-op se ja existe.
            // R6: em Production, exception aqui DERRUBA o startup. Painel admin inacessivel
            // por bug de config (env var ausente, senha fraca) e blocker — melhor falhar deploy
            // do que subir API silenciosamente quebrada.
            try
            {
                using var superSeedScope = app.Services.CreateScope();
                var superSeedDb = superSeedScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                // RLS: SuperAdmin seed cria registros sem tenant fixo — bypass obrigatório.
                using var _ = superSeedDb.UseRowLevelSecurityBypass();
                await SuperAdminSeed.ExecutarAsync(superSeedDb, app.Logger, app.Environment.IsProduction());
            }
            catch (Exception ex) when (!app.Environment.IsProduction())
            {
                app.Logger.LogError(ex, "Erro durante SuperAdminSeed (nao-Production, continuando). Painel admin pode ficar inacessivel.");
            }
            // Em Production: nao captura — exception sobe e derruba o startup com mensagem clara.

            // R6: SeedData popula tenants demo (PastaBella, CasaDaBaba, etc.) — proibido em Production.
            // Roda apenas se Development OU SEED_DEMO_DATA=true (opt-in explicito pra staging).
            // SuperAdminSeed e NotificacoesGlobaisSeed seguem rodando (sao infra, nao demo).
            var seedDemoEnabled = app.Environment.IsDevelopment()
                || string.Equals(Environment.GetEnvironmentVariable("SEED_DEMO_DATA"), "true", StringComparison.OrdinalIgnoreCase);
            if (seedDemoEnabled)
            {
                try
                {
                    using var seedScope = app.Services.CreateScope();
                    // RLS: SeedData percorre todos os tenants demo — bypass no DbContext
                    // do scope para que use cases internos enxerguem o universo todo.
                    var seedDb = seedScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                    using var __ = seedDb.UseRowLevelSecurityBypass();
                    await SeedData.ExecutarAsync(seedScope.ServiceProvider, app.Logger);
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Erro durante seed. Continuando sem seed.");
                }
            }
            else
            {
                app.Logger.LogInformation(
                    "[SeedData] Skipped — env={Env}, SEED_DEMO_DATA nao e 'true'. Demo seed bloqueado fora de Development (R6).",
                    app.Environment.EnvironmentName);
            }

            try
            {
                using var notifSeedScope = app.Services.CreateScope();
                var notifDb = notifSeedScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
                // RLS: catalogo de notificacoes globais (sem EmpresaId) + writes em
                // tabelas tenant-aware — bypass cobre os dois.
                using var _ = notifDb.UseRowLevelSecurityBypass();
                await NotificacoesGlobaisSeed.ExecutarAsync(notifDb, app.Logger);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Erro durante seed de notificações globais. Continuando.");
            }

            // Schema do módulo Casa da Baba Mobile (SQL raw, idempotente, fora do EF migrations).
            try
            {
                await Mobile.Schema.MobileSchemaInitializer.InitializeAsync(app.Services, app.Logger);
            }
            catch (Exception ex)
            {
                app.Logger.LogError(ex, "Falha ao aplicar Mobile schema. Endpoints /api/mobile/* vão falhar.");
            }
        }, CancellationToken.None);

        if (!acquired)
        {
            app.Logger.LogInformation(
                "[Startup] Outra replica detem advisory lock 0x{LockKey:X} — pulando migrations/seeds. Health check /health/ready confirmara consistencia.",
                LockKeys.StartupMigrationsAndSeed);
        }
    }
}
