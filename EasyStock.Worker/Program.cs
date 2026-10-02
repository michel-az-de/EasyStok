using EasyStock.Application.DependencyInjection;
using EasyStock.Infra.Async.DependencyInjection;
using EasyStock.Infra.Async.Storage;
using EasyStock.Infra.Notifications.DependencyInjection;
using EasyStock.Infra.Notifications.Hosting;
using EasyStock.Infra.Postgre.Concurrency;
using EasyStock.Infra.Postgre.DependencyInjection;
using EasyStock.Infra.Postgre.Notifications.Agendamento;
using EasyStock.Worker;
using EasyStock.Worker.BackgroundServices;
using EasyStock.Worker.DependencyInjection;
using EasyStock.Infra.Integrations.DependencyInjection;
using Serilog;

AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    Log.Fatal("UNHANDLED EXCEPTION (IsTerminating={IsTerminating}): {Exception}",
        e.IsTerminating, e.ExceptionObject);
    Log.CloseAndFlush();
};

TaskScheduler.UnobservedTaskException += (_, e) =>
{
    Log.Error("UNOBSERVED TASK EXCEPTION: {Exception}", e.Exception);
    e.SetObserved();
};

var builder = Host.CreateApplicationBuilder(args);

// Serilog
var loggerConfiguration = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext();

// Logs via OTLP quando ha collector configurado (issue 1002) — correlacionados por
// trace_id/span_id com os traces do TelemetrySetup. Opt-in: sem a config, sem sink.
if (builder.Configuration["OpenTelemetry:OtlpEndpoint"] is { Length: > 0 } otlpLogsEndpoint)
{
    loggerConfiguration = loggerConfiguration.WriteTo.OpenTelemetry(o =>
    {
        o.Endpoint = otlpLogsEndpoint;
        o.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
        o.ResourceAttributes = new Dictionary<string, object> { ["service.name"] = "EasyStock.Worker" };
    });
}

Log.Logger = loggerConfiguration.CreateLogger();

builder.Services.AddSerilog();

// Telemetria OTLP opt-in (issue 1002) — no-op sem OpenTelemetry:OtlpEndpoint.
builder.AddEasyStockTelemetry("EasyStock.Worker");

// Options — WorkerOptions mantido por retro-compat (lê seção "Worker"); seção canônica é
// "Notifications:Hosting" lida via AddNotificationsCore.
builder.Services.Configure<WorkerOptions>(
    builder.Configuration.GetSection(WorkerOptions.Section));

// Infrastructure
var connStr = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection não configurada.");

// AddEasyStockPostgreInfrastructure já registra os repositórios de notificação e o coletor (N1: chamar
// AddEasyStockNotificationsRepositories de novo fazia o coletor rodar duas vezes por rodada).
builder.Services.AddEasyStockPostgreInfrastructure(connStr, builder.Configuration);

// Email service: a mesma fabrica da API (Email:Provider e secao Smtp), para os dois hosts resolverem
// o mesmo provider e as mesmas opcoes (N3, #1351), sem chamar AddEasyStockAsyncInfrastructure completo.
builder.Services.AddEasyStockEmail(builder.Configuration);

// WhatsApp de plataforma (N6): mesma validacao da API; com o provider "meta", numero e verify token sao obrigatorios.
EasyStock.Infra.Notifications.Hosting.WhatsAppPlataformaStartup.Validar(builder.Configuration);

// Notifications infra (canal adapters + Scriban renderer)
builder.Services.AddNotificationsInfra(builder.Configuration);

// Application services + use cases (NotificadorService, RotinaScheduler, ResolvedorCanal, etc.)
builder.Services.AddEasyStockApplication();

// Advisory lock utility
builder.Services.AddScoped<PostgresAdvisoryLock>();

// Hosting do pipeline de notificações — coletores e dispatcher orchestrator já vêm de
// AddEasyStockNotificationsRepositories() acima. AddNotificationsHosting chama
// AddNotificationsCore internamente (idempotente), então não duplicar.
// Mode lido de "Notifications:Hosting:Mode".
builder.Services
    .AddNotificationsHosting(builder.Configuration)
    .AddPostgresOutboxSignaler(builder.Configuration);

// Lembretes de pedidos agendados (mobile_orders.scheduled_delivery_at):
// no dia, 1h antes, 10min antes. Idempotencia via colunas agendamento_notificado_*_em.
builder.Services.AddSingleton<LembretesPedidoAgendadoTick>();
builder.Services.AddHostedService<AgendamentoNotificacaoService>();

// Monitor de saude de endpoints publicos (N10). Abre incidente apos N falhas seguidas e avisa os superadmins por
// IPublicadorIncidenteSistema (e-mail e WhatsApp), com aviso de "normalizado" na recuperacao. Estado em
// endpoint_health_state; a dedupe de 15 min vem da chave de idempotencia do outbox. Precisa de
// Auth__Google__EmpresaPadrao no .env do Worker: sem ela nada e publicado e o log nomeia a chave.
builder.Services.AddHttpClient("endpoint-health");
builder.Services.AddHostedService<EndpointHealthMonitorService>();

// Limpeza agendada dos reset_tokens expirados ha mais de 24 h (N8, #301), a cada 6 h.
builder.Services.AddHostedService<LimpezaTokensAcessoService>();

// Outbox de eventos de integração externa (F4.c) — consome
// OutboxEventoIntegracao e despacha via handlers registrados.
// Pode ser desligado via Integration:Outbox:Enabled=false (default true).
builder.Services.AddHostedService<IntegrationOutboxBackgroundService>();

// Storage de arquivos (IFileStorage): o motor de relatórios escreve/lê artefatos via
// IFileStorage. As implementações vivem em Infra.Async.Storage (compartilhadas com a API).
// Sem este registro o Worker não resolvia IFileStorage e o ReportRunner falhava em runtime
// (gap fechado — provider lido de "FileStorage:Provider", default Local).
builder.Services.AddEasyStockFileStorageCore(builder.Configuration);

// Motor de relatórios assíncrono (PR-C0 — ADR-R02/R03/R04/R06/R07)
// Registra ReportRunnerBackgroundService + ReportWatchdogBackgroundService +
// WorkerCurrentUserAccessor (override ADR-R06) + ReportExecutionContext (AsyncLocal).
builder.Services.AddReportingWorker();

// IMemoryCache: a API registra via AddEasyStockCache; o Worker registra aqui para os
// servicos compartilhados que dependem dele. Singleton consumido por Scoped: OK.
builder.Services.AddMemoryCache();

// #877: registra ICacheService — sem isso o EstoqueSaldoCacheInvalidationInterceptor
// (adicionado ao DbContext incondicionalmente em AddEasyStockPostgreInfrastructure via
// ProdutoCacheInvalidator, commit f5d1ec23/#525) nao resolve e o EasyStockDbContext NAO
// constroi, derrubando TODO background service de banco do Worker (outbox, notificacoes,
// SLA, banners, agendamento, jobs de cobranca) por tick. RedisCacheService envelopa
// IDistributedCache; espelha a API sem Redis conn string (in-memory distribuido). TODO:
// extrair AddEasyStockCache p/ local compartilhado (API+Worker) e ligar Redis
// (ConnectionStrings__Redis) p/ invalidacao cross-processo com a API.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSingleton<EasyStock.Application.Ports.Output.ICacheService, EasyStock.Infra.Async.RedisCacheService>();

// Polly pipelines compartilhados pelas integracoes HTTP
builder.Services.AddEasyStockIntegrationResilience();
// Atendimento WhatsApp (S02) — cliente da Cloud API que MetaCloudWhatsAppProvider delega.
builder.Services.AddEasyStockWhatsAppCloudClient(builder.Configuration);
builder.Services.AddEasyStockMetaMensageria(builder.Configuration);
// Atendimento WhatsApp (S06) — o Worker reusa AddEasyStockApplication(), que registra o agente.
builder.Services.AddEasyStockAgenteLlm(builder.Configuration);
// Key ring compartilhado com a Api via Postgres (#1035) — sem isso o Worker nao
// decifra o certificado A1 que a Api gravou em credencial_integracao.
builder.Services.AddEasyStockDataProtection();

// Health checks do motor de notificacoes (N1): heartbeat dos loops e backlog. O Worker nao tem endpoint HTTP: quem
// consome e o HealthchecksPingService, que pinga o Healthchecks.io enquanto estao saudaveis e chama /fail quando nao.
builder.Services.AddHealthChecks()
    .AddNotificationsHosting()
    .AddNotificacoesBacklog();
builder.Services.AddHttpClient(HealthchecksPingService.NomeDoCliente);
builder.Services.AddHostedService<HealthchecksPingService>();

// Validação de DI sob demanda (CI/diagnóstico): `dotnet run -- --validate-di` constrói
// o grafo com ValidateOnBuild + ValidateScopes, pegando captive dependencies
// (singleton↔scoped) e serviços não-resolvíveis. Foi assim que o lifetime mismatch da
// factory fiscal (#194) passou silencioso no Generic Host (não valida por padrão).
//
// Esta ferramenta encontrou e fechamos 2 crashes latentes que dariam "erro genérico":
//   • IFileStorage  — motor de relatórios (ReportRunner/Watchdog). Realocado p/ Infra.Async.
//   • IMemoryCache  — cadeia fiscal (removida na poda P04, #1106).
//
// Mantido CONDICIONAL (não roda no startup normal): o grafo ainda não é 100% resolvível
// porque o Worker reusa AddEasyStockApplication() — que registra TODOS os use cases da
// API (auth, PIX, uploads, faturas, admin, inteligência, tickets). Esses use cases nunca
// são resolvidos pelos jobs do Worker (registros mortos), mas o ValidateOnBuild os reporta.
// Um gate incondicional verde exigiria fatiar AddEasyStockApplication para o Worker
// registrar só o que executa — refatoração de registro compartilhado (follow-up).
if (args.Contains("--validate-di"))
{
    using var validationProvider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
    {
        ValidateOnBuild = true,
        ValidateScopes = true
    });
    Log.Information("Worker: grafo de DI validado com sucesso.");
    Log.CloseAndFlush();
    return;
}

var host = builder.Build();
host.Run();
