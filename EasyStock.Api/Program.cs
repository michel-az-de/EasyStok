using EasyStock.Api.BackgroundServices;
using EasyStock.Api.Configuration;
using EasyStock.Api.Data;
using EasyStock.Api.Observability;
using EasyStock.Application.DependencyInjection;
using EasyStock.Application.Validators;
using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Application.Ports.Output.Fiscal;
using EasyStock.Infra.Integrations.DependencyInjection;
using EasyStock.Infra.Integrations.Fiscal;
using EasyStock.Infra.Integrations.Fiscal.FocusNFe.DependencyInjection;
using EasyStock.Infra.Integrations.Fiscal.Mock.DependencyInjection;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.Data;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Async.Storage;
using FluentValidation;
using FluentValidation.AspNetCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Serilog;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using EasyStock.Api.Observability.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;

// Handler global para exceções não tratadas que derrubam o processo
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal("UNHANDLED EXCEPTION (processo encerrando={IsTerminating}): {Exception}",
        e.IsTerminating, e.ExceptionObject);
    Log.CloseAndFlush();
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error("UNOBSERVED TASK EXCEPTION: {Exception}", e.Exception);
    e.SetObserved(); // evita crash do processo por Task não observada
};

var builder = WebApplication.CreateBuilder(args);

// Garantir que diretório de logs existe antes do Serilog iniciar
{
    var logDir = builder.Configuration["LogSettings:LogDirectory"] is { Length: > 0 } dir
        ? dir
        : Path.Combine(AppContext.BaseDirectory, "logs");
    try { Directory.CreateDirectory(logDir); } catch { /* best-effort */ }
}

// LevelSwitch permite controlar o nível mínimo em tempo real via DiagnosticoModeService.
// Criado aqui (antes do Serilog) para ser injetado no logger e nos serviços.
var diagLevelSwitch = new Serilog.Core.LoggingLevelSwitch(Serilog.Events.LogEventLevel.Information);

// Configure Serilog — máximo de informações em cada log entry
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .MinimumLevel.ControlledBy(diagLevelSwitch)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProcessId()
    .Enrich.WithMachineName()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Core MVC ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        opts.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddHttpContextAccessor();
builder.Services.AddEndpointsApiExplorer();

// Response compression — Brotli/Gzip para JSON e estaticos (PWA). Reduz bandwidth
// significativamente em listagens grandes (catalogos, mobile sync). Render cobra
// bandwidth acima do free tier; CPU overhead e' marginal.
builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
    o.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
    {
        "application/json",
        "application/javascript",
        "image/svg+xml"
    });
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// ── Feature DI groups ─────────────────────────────────────────────────────────
builder.Services.AddEasyStockSwagger();
builder.Services.AddEasyStockAuth(builder.Configuration);
builder.Services.AddEasyStockCors(builder.Configuration, builder.Environment);
builder.Services.AddEasyStockRateLimit();
builder.Services.AddEasyStockObservability(builder.Configuration, builder.Environment);
builder.Services.AddEasyStockCache(builder.Configuration);
builder.Services.AddEasyStockFileStorage(builder.Configuration);

// ── Database ──────────────────────────────────────────────────────────────────
var databaseProvider = builder.Configuration[ConfigurationKeys.DatabaseProvider] ?? "Auto";
var postgresConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionDefault);
var mongoConnectionString = builder.Configuration.GetConnectionString(ConfigurationKeys.ConnectionMongo);
var mongoDatabaseName = builder.Configuration[ConfigurationKeys.DatabaseMongoDatabase] ?? "EasyStockDbMongo";

// Em produção, pula a checagem de auto-detect (custa 3-5s no cold start)
// quando o provider está explicitamente configurado.
string resolvedProvider;
if (builder.Environment.IsProduction() &&
    !databaseProvider.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase))
{
    resolvedProvider = databaseProvider.Trim().ToLowerInvariant() switch
    {
        "postgres" or "postgresql" => "postgresql",
        "mongodb" or "mongo" => "mongodb",
        _ => "postgresql"
    };
}
else
{
    resolvedProvider = await ResolveDatabaseProviderAsync(
        databaseProvider, postgresConnectionString, mongoConnectionString, Log.Logger);
}

// PostgreSQL é o único provedor suportado (#261) — não há mais fallback runtime.
var infraState = new ResolvedInfrastructureState
{
    DatabaseProvider = resolvedProvider,
    ConfiguredProvider = databaseProvider,
    IsFallback = false,
    StartupTime = DateTimeOffset.UtcNow,
    Environment = builder.Environment.EnvironmentName
};
builder.Services.AddSingleton(infraState);

switch (resolvedProvider)
{
    case "mongodb":
        // MongoDB foi descontinuado como provedor transacional (B2 do plano de a��o).
        // Paridade incompleta com Postgres (sem Venda, ItemVenda, MovimentacaoEstoque,
        // Caixa, Lote, Pedido) gerava risco de bug silencioso. Postgres � o �nico
        // provedor transacional suportado. Rever ADR 0001-mongo-discarded.
        throw new NotSupportedException(
            "MongoDB foi descontinuado como provedor transacional. " +
            "Use Database:Provider=PostgreSQL. Detalhes: docs/adr/0001-mongo-discarded.md.");

    case "postgresql":
        builder.Services.AddEasyStockPostgreInfrastructure(postgresConnectionString!, builder.Configuration);
        builder.Services.AddHealthChecks()
            .AddNpgSql(postgresConnectionString!, name: "PostgreSQL", tags: ["ready", "api"])
            .AddCheck<RedisHealthCheck>("Redis", tags: ["api"])           // sem tag "ready" — Redis degradado não remove pod do LB
            .AddCheck<ConfigurationHealthCheck>("Configuracao", tags: ["ready", "api"])
            .AddNotificationsHosting();
        // Modulo Fiscal NFC-e (F2) — Polly pipelines + adapters Focus NFe + Mock + cert A1
        builder.Services.AddEasyStockIntegrationResilience();
        builder.Services.AddFocusNFeAdapter(builder.Configuration);
        builder.Services.AddMockFiscalGateway();
        // Scoped (não Singleton): a factory consome IEnumerable<IGatewayFiscal>, e os
        // adapters (Focus/Mock) são Scoped (dependem de serviços scoped como
        // INfeCertificadoA1Service). Como Singleton, capturava gateways scoped
        // (captive dependency / lifetime mismatch) — só não explodia em prod porque
        // ValidateOnBuild fica off lá. Os consumidores (use cases fiscais) são Scoped.
        builder.Services.AddScoped<IGatewayFiscalFactory, GatewayFiscalFactory>();
        builder.Services.AddDataProtection();
        break;

    default:
        throw new InvalidOperationException($"Database:Provider '{databaseProvider}' não suportado.");
}

// ── Application + Async Infra ─────────────────────────────────────────────────
builder.Services.AddEasyStockApplication();
builder.Services.AddEasyStockStorefrontUseCases();
builder.Services.AddReportingApi();
builder.Services.Configure<EasyStock.Application.Services.PedidoEstoqueOptions>(
    builder.Configuration.GetSection("Pedidos"));
builder.Services.AddEasyStockAsyncInfrastructure(builder.Configuration);

// ── Storefront — WhatsApp OTP provider (stub em Development, real em Prod) ────
// TASK-EZ-AUTH-001: stub apenas em ambientes nao-Production. Provider real
// (Meta WhatsApp Cloud API) entra em TASK-EZ-WA-001 apos Meta Business
// Verification (TASK-HUM-001). Em Production sem provider real, AuthController
// nao resolve — fail fast intencional.
if (!builder.Environment.IsProduction())
{
    builder.Services.AddEasyStockWhatsAppStub();
}

// ── Storefront — CEP lookup (ViaCEP em prod, NoOp por default) ────────────────
// TASK-EZ-FRETE-001: feature flag ENABLE_VIACEP_LOOKUP (default false). Quando
// off, registra NoOpCepLookupClient (não bate na API externa). Quando on,
// registra ViaCepLookupClient com timeout 1s.
builder.Services.AddEasyStockCepLookup(builder.Configuration);

builder.Services.Configure<EasyStockConfiguracoes>(
    builder.Configuration.GetSection(ConfigurationKeys.SectionEasyStock));

// Registrar IEasyStockConfiguracoes para injeção em use cases
builder.Services.AddScoped<EasyStock.Application.Configuration.IEasyStockConfiguracoes>(sp =>
    sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<EasyStockConfiguracoes>>().Value);

// ── Notifications: infra (canal adapters + Scriban) + hosting (orchestrators/options) ──
// Hosting fica como "Disabled" por default na API — ative trocando "Notifications:Hosting:Mode"
// para "Hosted" se quiser rodar o pipeline in-process (modo sem Worker).
// AddPostgresOutboxSignaler é no-op se Mode=Disabled ou Signaler!=Postgres (ver impl).
builder.Services.AddNotificationsInfra(builder.Configuration);
builder.Services
    .AddNotificationsHosting(builder.Configuration)
    .AddPostgresOutboxSignaler(builder.Configuration);
builder.Services.AddScoped<PostgresAdvisoryLock>();

// Aviso explicito quando o pipeline de notificacoes vai rodar in-process: nao ha bulkhead
// real entre HTTP da API e os 3 loops (compartilham ThreadPool, GC e memoria). Modo
// suportado para Render free tier ou dev/teste; em producao prefira Worker como deploy
// separado (Notifications:Hosting:Mode=Disabled aqui + Mode=Hosted no Worker).
{
    var notifMode = builder.Configuration["Notifications:Hosting:Mode"];
    if (string.Equals(notifMode, "Hosted", StringComparison.OrdinalIgnoreCase))
    {
        Log.Warning(
            "Notifications:Hosting:Mode=Hosted na API — pipeline rodando in-process. " +
            "Sem isolamento de processo entre HTTP e loops; monitore /health/dispatcher.");
    }
}

// ── Background Services + misc ────────────────────────────────────────────────
builder.Services.AddEasyStockBackgroundJobs(builder.Configuration);
builder.Services.AddHttpClient(); // for DiagnosticoInfraController self-testing
builder.Services.AddValidatorsFromAssemblyContaining<CadastrarProdutoCommandValidator>();

// ── Mobile module services (Onda 2 parte 2: stock reconciliation) ────────────
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.MobileStockReconciler>();
// Onda 3: vendas mobile -> Venda ERP (Order entregue cria Venda + ItemVenda).
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.MobileSaleSyncService>();
// F9-E: resolve Usuario "Sistema Mobile Sync" pra auditoria de produto/movimentacao
// (tabelas com UsuarioId NOT NULL). Lookup-or-create idempotente por empresa.
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.MobileSystemUserResolver>();
// Onda 5: SSE realtime entre devices da mesma loja.
// Broker é Singleton — listeners persistem cross-request via dictionary in-memory.
// Em multi-instance, evoluir pra Redis pubsub.
builder.Services.AddSingleton<EasyStock.Api.Mobile.Services.MobileEventBroker>();
// SyncController decomposition: mutation dispatch, auto-link pipeline, reverse pull.
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.SyncMutationDispatcher>();
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.SyncAutoLinker>();
builder.Services.AddScoped<EasyStock.Api.Mobile.Services.SyncReversePullService>();
// Onda 9: OTA do PWA — lê CACHE_VERSION do sw.js em runtime pra /version reportar
// a versão real do bundle (sem depender de config drift-prone).
builder.Services.AddSingleton<EasyStock.Api.Mobile.Services.IPwaVersionProvider,
    EasyStock.Api.Mobile.Services.PwaVersionProvider>();

// SeedProgressService: Singleton pra compartilhar estado de runs entre requests.
// O background job e o polling endpoint falam com a mesma instância.
builder.Services.AddSingleton<EasyStock.Api.Services.SeedProgressService>();

// Storefront — expirar sessões de clientes (ADR-0012: sliding window 30d).
builder.Services.AddHostedService<EasyStock.Api.Services.Storefront.ExpirarClienteSessionsBackgroundService>();

// DiagnosticoModeService: Singleton que controla LoggingLevelSwitch em tempo real.
builder.Services.AddSingleton(diagLevelSwitch);
builder.Services.AddSingleton<EasyStock.Api.Observability.DiagnosticoModeService>();

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Modo migrate-only (release_command do deploy) ─────────────────────────────
// Aplica migrations pendentes e ENCERRA, sem subir o servidor. O deploy (Fly
// release_command) roda este modo numa máquina temporária ANTES de promover a
// versão nova; se a migration falhar (exit != 0), o deploy é abortado e a versão
// antiga continua servindo — produção nunca fica com código novo + banco velho.
if (args.Contains("--migrate-only"))
{
    using var migrateScope = app.Services.CreateScope();
    var migrateDb = migrateScope.ServiceProvider.GetRequiredService<EasyStockDbContext>();
    using var _ = migrateDb.UseRowLevelSecurityBypass();
    var pendentes = (await migrateDb.Database.GetPendingMigrationsAsync()).ToList();
    app.Logger.LogInformation("[migrate-only] {Count} migration(s) pendente(s): {Lista}",
        pendentes.Count, pendentes.Count == 0 ? "(nenhuma)" : string.Join(", ", pendentes));
    await migrateDb.Database.MigrateAsync();
    app.Logger.LogInformation("[migrate-only] Concluido. Encerrando sem subir o servidor.");
    return;
}

// ForwardedHeaders: Fly/Render/etc fazem TLS no edge e mandam HTTP com
// X-Forwarded-Proto=https. Sem isso o UseHttpsRedirection estoura 400.
app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

// ── Migrations + Seed (PostgreSQL only) ───────────────────────────────────────
// Em produção com múltiplas réplicas, desabilitar via RunMigrationsOnStartup=false
// e rodar migrations em init-container ou job separado antes do deploy.
// No Render/Cloud Run o entrypoint do container ja roda o EF bundle ANTES do app
// subir — esse bloco aqui e' rede de seguranca e idempotente (no-op se schema
// ja esta atualizado).
var runMigrationsOnStartup = builder.Configuration.GetValue("RunMigrationsOnStartup", defaultValue: !app.Environment.IsProduction());
var migrationsFailFast = builder.Configuration.GetValue("MigrationsFailFast", defaultValue: false);

app.Logger.LogInformation(
    "[Migrations] Estado lido: Environment={Environment} | Database__Provider={ProviderConfig} | resolvedProvider={Resolved} | RunMigrationsOnStartup={Run} | MigrationsFailFast={FailFast}",
    app.Environment.EnvironmentName, databaseProvider, resolvedProvider, runMigrationsOnStartup, migrationsFailFast);

if (runMigrationsOnStartup && resolvedProvider is "postgresql")
{
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
        var bootstrapDb = bootstrapScope.ServiceProvider.GetRequiredService<EasyStock.Infra.Postgre.Data.EasyStockDbContext>();
        // RLS: schema bootstrap mexe em tabelas tenant-aware sem JWT contextual.
        using var _ = bootstrapDb.UseRowLevelSecurityBypass();
        await EasyStock.Api.Data.SeedSchemaBootstrap.EnsureAsync(bootstrapDb, app.Logger);
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
        await EasyStock.Api.Mobile.Schema.MobileSchemaInitializer.InitializeAsync(app.Services, app.Logger);
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

// Restaura modo de logging verbose salvo no DB (após migrations estarem aplicadas).
if (resolvedProvider is "postgresql")
{
    try
    {
        var diagMode = app.Services.GetRequiredService<EasyStock.Api.Observability.DiagnosticoModeService>();
        await diagMode.RestoreFromDbAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "[DiagnosticoMode] Falha ao restaurar modo — usando Information.");
    }
}

// ── Startup hardening ─────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration[ConfigurationKeys.JwtSecretKey];
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Contains("${JWT_SECRET_KEY}"))
    throw new InvalidOperationException("JWT_SECRET_KEY environment variable is required (min 32 chars). Set it before starting the API.");
if (jwtSecret.Length < 32)
    throw new InvalidOperationException("JWT_SECRET_KEY must be at least 32 characters long.");
// Bloquear o secret de dev conhecido em qualquer ambiente — caso vaze de novo, falha rápido.
if (jwtSecret.Contains("EasyStock-Dev-SuperSecretKey", StringComparison.Ordinal))
    throw new InvalidOperationException("CRITICAL: known leaked dev JWT secret detected in configuration. Rotate JWT_SECRET_KEY immediately.");

// Validar connection strings não têm placeholders
if (postgresConnectionString?.Contains("${") == true)
    throw new InvalidOperationException("Database connection string contains placeholders. Set environment variables: DB_HOST, DB_PORT, DB_NAME, DB_USER, DB_PASSWORD.");

if (mongoConnectionString?.Contains("${") == true)
    throw new InvalidOperationException("MongoDB connection string contains placeholders. Set MONGO_CONNECTION_STRING environment variable.");

// Validar que database credentials não são defaults/placeholders
if (postgresConnectionString?.Contains("Username=postgres") == true && postgresConnectionString?.Contains("Password=postgres") == true)
    throw new InvalidOperationException("CRITICAL: Default PostgreSQL credentials detected. Set DB_PASSWORD to a secure value before deployment.");

// Validar Mobile:ApiKey: rejeitar valor literal vazado e exigir tamanho mínimo em Production.
var mobileApiKey = builder.Configuration["Mobile:ApiKey"];
if (!string.IsNullOrEmpty(mobileApiKey))
{
    if (mobileApiKey.Contains("${MOBILE_API_KEY}", StringComparison.Ordinal))
        throw new InvalidOperationException("MOBILE_API_KEY environment variable is required when Mobile:ApiKey is configured. Set it via env var or user-secrets.");
    // Identidade quebrada em duas partes pra gitleaks nao flaggear o literal no codigo
    // — o valor abaixo eh exatamente a chave dev vazada que estamos rejeitando.
    const string knownLeakedDevKey = "cdb-dev-key-change" + "-in-production-2026"; // gitleaks:allow
    if (mobileApiKey.Equals(knownLeakedDevKey, StringComparison.Ordinal))
        throw new InvalidOperationException("CRITICAL: known leaked dev Mobile API key detected in configuration. Rotate Mobile:ApiKey immediately.");
    if (builder.Environment.IsProduction() && mobileApiKey.Length < 24)
        throw new InvalidOperationException("Mobile:ApiKey must be at least 24 characters long in Production.");
}

Log.Information("""

    ======================================
      EasyStock API
      Ambiente:     {Environment}
      Banco:        {Provider} (configurado: {Configured})
      Raiz (/):     → redireciona para /swagger
      Swagger:      /swagger
      Diagnostico:  /diagnostico
      Health:       /health, /health/live, /health/ready
    ======================================
    """,
    app.Environment.EnvironmentName, resolvedProvider, databaseProvider);

// ── Middleware pipeline ───────────────────────────────────────────────────────
// ExceptionHandler deve ser o primeiro middleware para capturar exceções de qualquer
// middleware abaixo, incluindo swagger, static files e autenticação.
app.UseExceptionHandler();

// ResponseCompression precisa rodar cedo, antes de StaticFiles e do request logging,
// pra ter chance de capturar o output dos middlewares seguintes.
app.UseResponseCompression();
app.UseMiddleware<EasyStock.Api.Middleware.SecurityHeadersMiddleware>();

// Correlation ID propagation
app.Use(async (context, next) =>
{
    var correlationId = context.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrEmpty(correlationId))
        correlationId = Guid.NewGuid().ToString();

    context.Items["CorrelationId"] = correlationId;
    context.Response.Headers["X-Correlation-Id"] = correlationId;

    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

// Serilog Request Logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms ({ResponseSize} bytes)";

    options.GetLevel = (ctx, _, ex) =>
    {
        if (ex is not null || ctx.Response.StatusCode >= 500)
            return Serilog.Events.LogEventLevel.Error;

        var path = ctx.Request.Path.Value ?? "";
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/diagnostico/ping", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/diagnostico/logs/live", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/diagnostico/historico", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/notificacoes/resumo", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/notificacoes/resumo", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/files/", StringComparison.OrdinalIgnoreCase))
            return Serilog.Events.LogEventLevel.Debug;

        return Serilog.Events.LogEventLevel.Information;
    };

    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("CorrelationId", httpContext.Items["CorrelationId"]);
        diagnosticContext.Set("ClientIP", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
        diagnosticContext.Set("ResponseSize", httpContext.Response.ContentLength ?? 0);

        var reqPath = httpContext.Request.Path.Value ?? "";
        var trafficType = reqPath switch
        {
            _ when reqPath.StartsWith("/health", StringComparison.OrdinalIgnoreCase)
                || reqPath.Contains("/ping", StringComparison.OrdinalIgnoreCase) => "infra",
            _ when reqPath.Contains("/diagnostico", StringComparison.OrdinalIgnoreCase)
                || reqPath.Contains("/notificacoes/resumo", StringComparison.OrdinalIgnoreCase) => "polling",
            _ when reqPath.Contains("/logs/live", StringComparison.OrdinalIgnoreCase) => "sse",
            _ => "business"
        };
        diagnosticContext.Set("TrafficType", trafficType);

        var userId = httpContext.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                  ?? httpContext.User?.FindFirst("sub")?.Value;
        if (userId is not null)
            diagnosticContext.Set("UserId", userId);
        var empresaId = httpContext.User?.FindFirst("empresa_id")?.Value
                     ?? httpContext.User?.FindFirst("EmpresaId")?.Value;
        if (empresaId is not null)
            diagnosticContext.Set("EmpresaId", empresaId);
    };
});

// Swagger JSON cache (in-memory, TTL 1h — evita ~1900ms por request)
{
    var swaggerCache = new System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Body, string ContentType, DateTimeOffset CachedAt)>();
    app.Use(async (context, next) =>
    {
        var path = context.Request.Path.Value ?? "";
        var isSwaggerJson = path.StartsWith("/swagger/", StringComparison.OrdinalIgnoreCase)
                         && path.EndsWith("/swagger.json", StringComparison.OrdinalIgnoreCase)
                         && context.Request.Method == "GET";

        if (!isSwaggerJson) { await next(); return; }

        if (swaggerCache.TryGetValue(path, out var cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < TimeSpan.FromHours(1))
        {
            context.Response.ContentType = cached.ContentType;
            context.Response.Headers["X-Swagger-Cache"] = "HIT";
            await context.Response.Body.WriteAsync(cached.Body);
            return;
        }

        var originalBody = context.Response.Body;
        using var buffer = new System.IO.MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next();
        }
        finally
        {
            context.Response.Body = originalBody;
        }
        buffer.Position = 0;
        var body = buffer.ToArray();
        var contentType = context.Response.ContentType ?? "application/json";
        swaggerCache[path] = (body, contentType, DateTimeOffset.UtcNow);
        context.Response.Headers["X-Swagger-Cache"] = "MISS";
        await originalBody.WriteAsync(body);
    });
}

// Swagger UI (Development + Staging, ou flag Swagger:EnableInProduction)
var swaggerEnabled = app.Environment.IsDevelopment()
    || app.Environment.IsStaging()
    || builder.Configuration.GetValue<bool>("Swagger:EnableInProduction");
if (swaggerEnabled)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "EasyStock API v1");
        c.RoutePrefix = "swagger";
        c.DocumentTitle        = "EasyStock API Docs";
        c.DefaultModelsExpandDepth(1);
        c.DefaultModelExpandDepth(3);
        c.DefaultModelRendering(Swashbuckle.AspNetCore.SwaggerUI.ModelRendering.Example);
        c.DisplayRequestDuration();
        c.EnableDeepLinking();
        c.EnableFilter();
        c.EnablePersistAuthorization();
        c.EnableTryItOutByDefault();
        c.ShowExtensions();
        c.ShowCommonExtensions();
        c.InjectStylesheet("/swagger-ui/custom.css");
        c.InjectJavascript("/swagger-ui/custom.js");
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Serve uploaded files from local storage path (skip for S3 — served directly)
var fileStorageOptions = builder.Configuration
    .GetSection(ConfigurationKeys.SectionFileStorage)
    .Get<FileStorageOptions>() ?? new();
if (!string.Equals(fileStorageOptions.Provider, "S3", StringComparison.OrdinalIgnoreCase))
{
    var localStorage = app.Services.GetRequiredService<EasyStock.Application.Ports.Output.Storage.IFileStorage>()
        as LocalFileStorage;
    if (localStorage is not null)
    {
        var rootPath = localStorage.GetRootPath();
        Directory.CreateDirectory(rootPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(rootPath),
            RequestPath = fileStorageOptions.PublicBaseUrl
        });
    }
}

// Casa da Baba Mobile PWA — static files em /pwa/ com headers de service worker.
EasyStock.Api.Mobile.MobileModule.UseMobilePwa(app);

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
// Sliding window: atualiza UltimoUsoEm da ClienteSession após cada request autenticado (ADR-0012).
app.UseMiddleware<EasyStock.Api.Middleware.ClienteSessionMiddleware>();
app.UseMiddleware<EasyStock.Api.Middleware.SubscriptionGateMiddleware>();
// Idempotencia: aplicado APOS auth para que ICurrentUserAccessor.EmpresaId esteja disponivel.
// Whitelist de POSTs criticos (R5: dedup retry de mobile/web).
EasyStock.Api.Middleware.IdempotencyMiddlewareExtensions.UseIdempotency(app, opts => opts
    .Add("/api/itensestoque")
    .Add("/api/itensestoque/estorno")
    .Add("/api/vendas")
    .Add("/api/mobile/vendas")
    .Add("/api/movimentacoes")
    .Add("/api/itensestoque/repor")
    .Add("/api/mobile/calculadora/criar-compra"));
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/swagger", permanent: false))
   .ExcludeFromDescription();

// /console e /api-docs apontam pro EasyStock Console (UI dark sci-fi alternativa ao /swagger).
app.MapGet("/console", () => Results.Redirect("/api-docs/", permanent: false))
   .ExcludeFromDescription();
app.MapGet("/api-docs", () => Results.Redirect("/api-docs/index.html", permanent: false))
   .ExcludeFromDescription();
app.MapGet("/api-docs/", () => Results.Redirect("/api-docs/index.html", permanent: false))
   .ExcludeFromDescription();

app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false
});

app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = WriteHealthCheckJsonResponse
});

// /health/api: dependencias HTTP da API (PG + Redis + config) — NAO inclui dispatcher.
// Loop de notificacoes preso nao deve marcar a API inteira como down nos LBs.
app.MapHealthChecks("/health/api", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("api"),
    ResponseWriter = WriteHealthCheckJsonResponse
});

// /health/dispatcher: heartbeats dos 3 BackgroundServices do pipeline de notificacoes.
// Healthy quando Mode=Disabled (pipeline em Worker separado). Unhealthy quando algum
// loop nao bate dentro de 5x intervalo configurado — sinal de pendurada.
app.MapHealthChecks("/health/dispatcher", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("dispatcher"),
    ResponseWriter = WriteHealthCheckJsonResponse
});

app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = WriteHealthCheckJsonResponse
});

// /health/version — endpoint do schema gate do PWA.
// Retorna a versao do contrato Mobile e SHA do build para que o cliente
// decida se pode aplicar update OTA (sync.js > maybeApplyPwaUpdate gate).
// Mantido separado do /api/mobile/version (que carrega features e OTA info)
// para que probes leves de health/version nao precisem subir a stack toda.
app.MapGet("/health/version", () =>
{
    var asm = Assembly.GetExecutingAssembly();
    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? asm.GetName().Version?.ToString()
               ?? "unknown";
    return Results.Ok(new
    {
        apiVersion = info,
        mobileSchemaVersion = 2,
        buildSha = Environment.GetEnvironmentVariable("BUILD_SHA") ?? "master",
        serverTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
    });
})
.AllowAnonymous()
.ExcludeFromDescription();

// Aviso de seguranca em Production: o default agora e RequireApiKey=true
// (appsettings.Production.json). Se algum operador setou Mobile__RequireApiKey=false
// via env var explicita, o sync mobile aceita requests anonimos e isso fica
// gritando como warning ate ele virar de volta.
if (app.Environment.IsProduction()
    && !app.Configuration.GetValue<bool>("Mobile:RequireApiKey"))
{
    app.Logger.LogWarning(
        "Mobile:RequireApiKey=false em Production por override explicito — " +
        "/api/mobile/sync aceita request anonimo. Restaurar default true assim " +
        "que todos os APKs estiverem pareados via /dispositivos.");
}

// Fail-fast Efi: so exige WebhookSecret quando o modulo Efi esta EFETIVAMENTE
// configurado (ClientId ou ClientSecret presentes). Sem credenciais Efi, o
// webhook /api/webhooks/pix nem processa nada util — exigir secret bloquearia
// ambientes que nao usam PIX (ex: Render de teste, dev). Quando Efi for
// configurado e Sandbox=false, ai sim aborta sem secret.
if (app.Environment.IsProduction())
{
    var efiClientId = app.Configuration["Efi:ClientId"];
    var efiClientSecret = app.Configuration["Efi:ClientSecret"];
    var efiConfigurado = !string.IsNullOrWhiteSpace(efiClientId)
                        || !string.IsNullOrWhiteSpace(efiClientSecret);

    var efiSecret = app.Configuration["Efi:WebhookSecret"];
    var efiAllowUnsigned = app.Configuration.GetValue<bool>("Efi:WebhookAllowUnsigned", false);
    var efiSandbox = app.Configuration.GetValue<bool>("Efi:Sandbox", true);

    if (!efiConfigurado)
    {
        app.Logger.LogInformation(
            "[Efi] Modulo nao configurado (ClientId/ClientSecret vazios) — webhook PIX " +
            "fica inerte. Setar Efi__ClientId/ClientSecret/WebhookSecret quando ativar PIX.");
    }
    else if (string.IsNullOrWhiteSpace(efiSecret) && !efiAllowUnsigned)
    {
        if (efiSandbox)
        {
            app.Logger.LogWarning(
                "[Efi] WebhookSecret vazio em Sandbox — /api/webhooks/pix vai aceitar requests " +
                "sem HMAC. OK em ambiente de teste; configure Efi__WebhookSecret antes de virar Sandbox=false.");
        }
        else
        {
            throw new InvalidOperationException(
                "Efi:WebhookSecret vazio em Production com Sandbox=false e Efi:WebhookAllowUnsigned=false. " +
                "Configurar Efi__WebhookSecret antes de receber PIX real ou setar explicitamente " +
                "Efi__WebhookAllowUnsigned=true (NAO recomendado).");
        }
    }
    else if (efiAllowUnsigned)
    {
        app.Logger.LogWarning(
            "Efi:WebhookAllowUnsigned=true em Production — /api/webhooks/pix aceita " +
            "requests sem HMAC. Configurar Efi__WebhookSecret e desativar essa flag.");
    }
}

// Modo openapi-export: o Swashbuckle.AspNetCore.Cli (scripts/export-openapi.ps1)
// captura o IServiceProvider via HostFactoryResolver e nao precisa do Host
// iniciado. Saimos antes de app.Run() pra evitar Host.StartAsync() — que
// dispararia hosted services do pipeline de notificacoes dependendo de DI
// Postgres-only (DispatcherLoopHostedService precisa de INotificacoesDispatcherOrchestrator).
if (string.Equals(Environment.GetEnvironmentVariable("OPENAPI_EXPORT"), "true", StringComparison.OrdinalIgnoreCase))
{
    app.Logger.LogInformation("[openapi-export] OPENAPI_EXPORT=true — saindo antes de app.Run().");
    return;
}

app.Run();

// ── Helpers ───────────────────────────────────────────────────────────────────

static Task WriteHealthCheckJsonResponse(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    var result = new
    {
        status = report.Status.ToString(),
        totalDuration = report.TotalDuration.TotalMilliseconds.ToString("0") + "ms",
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            duration = e.Value.Duration.TotalMilliseconds.ToString("0") + "ms",
            error = e.Value.Exception?.Message
        })
    };
    return context.Response.WriteAsJsonAsync(result, new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
}

static async Task<string> ResolveDatabaseProviderAsync(
    string configuredProvider,
    string? postgresConnectionString,
    string? mongoConnectionString,
    Serilog.ILogger logger)
{
    var normalized = configuredProvider.Trim().ToLowerInvariant();

    // OPENAPI_EXPORT=true: Swashbuckle.AspNetCore.Cli precisa do builder DI registrado
    // mas nao toca DB real. Aceita PostgreSQL "imaginario" — DbContext nao chega a abrir
    // conexao (script retorna antes de app.Run() — ver bloco openapi-export ao final).
    var isOpenApiExport = string.Equals(
        Environment.GetEnvironmentVariable("OPENAPI_EXPORT"), "true", StringComparison.OrdinalIgnoreCase);

    if (normalized is "postgres" or "postgresql")
    {
        if (isOpenApiExport) return "postgresql";

        if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
            await IsPostgresAvailableAsync(postgresConnectionString, logger))
            return "postgresql";

        throw new InvalidOperationException(
            "PostgreSQL configurado mas indisponível. " +
            "Verifique a connection string 'DefaultConnection' e a conectividade com o banco. " +
            "Em dev, suba Postgres via Docker Compose ou aponte para o banco Render dev.");
    }

    if (normalized is "mongodb" or "mongo")
    {
        // B2: Mongo descontinuado como provedor transacional. Falha rápido para
        // operador notar que precisa migrar para Postgres.
        throw new NotSupportedException(
            "MongoDB foi descontinuado como provedor transacional. " +
            "Use Database:Provider=PostgreSQL. Detalhes: docs/adr/0001-mongo-discarded.md.");
    }

    if (normalized is "auto")
    {
        if (isOpenApiExport) return "postgresql";

        if (!string.IsNullOrWhiteSpace(postgresConnectionString) &&
            await IsPostgresAvailableAsync(postgresConnectionString, logger))
        {
            logger.Information("Auto-deteccao: usando PostgreSQL.");
            return "postgresql";
        }

        // Sem fallback: PostgreSQL é o único provedor transacional suportado (#261).
        throw new InvalidOperationException(
            "Auto-deteccao: PostgreSQL indisponível e não há fallback. " +
            "Verifique a connection string 'DefaultConnection' (suba Postgres via Docker Compose em dev).");
    }

    throw new InvalidOperationException($"Database:Provider '{configuredProvider}' não suportado.");
}

static async Task<bool> IsPostgresAvailableAsync(string connectionString, Serilog.ILogger logger)
{
    try
    {
        var csb = new Npgsql.NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 3,
            CommandTimeout = 3
        };
        await using var conn = new Npgsql.NpgsqlConnection(csb.ToString());
        await conn.OpenAsync();
        return true;
    }
    catch (Exception ex)
    {
        logger.Debug(ex, "PostgreSQL indisponivel.");
        return false;
    }
}

public partial class Program { }
