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

var app = builder.Build();

// === Pipeline + redirects + Razor Pages (verbatim em Hosting/AdminPipelineExtensions.cs) ===
app.UseEasyStockAdminPipeline();

// === 34 proxies /api-proxy/* (verbatim em Hosting/ApiProxyEndpoints.cs) ===
app.MapAdminApiProxies();
app.Run();

// Torna Program acessivel para WebApplicationFactory<Program> em EasyStock.Admin.UnitTests.
public partial class Program;
