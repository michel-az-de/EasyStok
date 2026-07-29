using EasyStock.Admin.DependencyInjection;
using EasyStock.Admin.Hosting;

var builder = WebApplication.CreateBuilder(args);

// â”€â”€ Globalization â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
var ptBR = new System.Globalization.CultureInfo("pt-BR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ptBR;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ptBR;

// â”€â”€ Services â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
// === Services (DI) ===
builder.AddEasyStockAdminServices();

// Telemetria OTLP opt-in (issue 1002) — no-op sem OpenTelemetry:OtlpEndpoint.
builder.AddEasyStockTelemetry("EasyStock.Admin");

var app = builder.Build();

// === Pipeline + redirects + Razor Pages (verbatim em Hosting/AdminPipelineExtensions.cs) ===
app.UseEasyStockAdminPipeline();

// === 34 proxies /api-proxy/* (verbatim em Hosting/ApiProxyEndpoints.cs) ===
app.MapAdminApiProxies();

// === Health check proprio do Admin ===
// Espelha o /health do Web (#453): expoe version (da assembly) + commit (ARG GIT_SHA do
// build, ja injetado pelo Dockerfile.cloudrun.admin / docker-compose.azure.yml). Sem isto o
// Admin nao tinha como provar qual commit esta no ar - toda triagem de QA ficava cega a
// "stale". Anonimo de proposito: o stale-check roda sem sessao.
var healthVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
var healthCommit = Environment.GetEnvironmentVariable("GIT_SHA");
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    version = healthVersion is not null
        ? $"{healthVersion.Major}.{healthVersion.Minor}.{healthVersion.Build}"
        : "unknown",
    commit = string.IsNullOrWhiteSpace(healthCommit) ? "unknown" : healthCommit
})).AllowAnonymous();

app.Run();

// Torna Program acessivel para WebApplicationFactory<Program> em EasyStock.Admin.UnitTests.
public partial class Program;
