using System.Globalization;
using System.Net.Http.Headers;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// 1. Cultura pt-BR global
var ptBR = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = ptBR;
CultureInfo.DefaultThreadCurrentUICulture = ptBR;

// 2. Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(o =>
{
    o.IdleTimeout = TimeSpan.FromMinutes(config.GetValue<int>("Session:IdleTimeoutMinutes"));
    o.Cookie.HttpOnly = true;
    o.Cookie.IsEssential = true;
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
    o.Cookie.Name = config["Session:CookieName"]!;
});

// 3. HttpContextAccessor (needed by SessionService inside TokenRefreshHandler)
builder.Services.AddHttpContextAccessor();

// 4. Register SessionService first (used by TokenRefreshHandler)
builder.Services.AddScoped<SessionService>();

// 5. HttpClient with TokenRefreshHandler
builder.Services.AddScoped<TokenRefreshHandler>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = config["ApiSettings:BaseUrl"]!;
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.GetValue<int>("ApiSettings:TimeoutSeconds"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
}).AddHttpMessageHandler<TokenRefreshHandler>();

// 5b. HttpClient for diagnostics (no TokenRefreshHandler - works without auth)
builder.Services.AddHttpClient<DiagnosticoWebService>(client =>
{
    var baseUrl = config["ApiSettings:BaseUrl"]!;
    // Ensure base URL ends with /
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
});

// 6. Domain services
builder.Services.AddScoped<ProdutosService>();
builder.Services.AddScoped<EstoqueService>();
builder.Services.AddScoped<EntradasService>();
builder.Services.AddScoped<SaidasService>();
builder.Services.AddScoped<FornecedoresService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<InteligenciaService>();
builder.Services.AddScoped<NotificacoesService>();
builder.Services.AddScoped<UsuariosService>();
builder.Services.AddScoped<AssinaturaService>();
builder.Services.AddScoped<ConfiguracoesService>();
builder.Services.AddScoped<AnunciosService>();
builder.Services.AddScoped<CategoriasService>();
builder.Services.AddScoped<LojasService>();
builder.Services.AddScoped<BuscaUnificadaService>();
builder.Services.AddScoped<InteligenciaLojasService>();

// 7. MVC + Antiforgery automático
builder.Services.AddControllersWithViews(o =>
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// 8. Cookie Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/auth/login";
        o.LogoutPath = "/auth/logout";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(480);
        o.SlidingExpiration = true;
    });

var app = builder.Build();

// Startup: verificar conectividade com a API (não-bloqueante)
_ = Task.Run(async () =>
{
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    var baseUrl = app.Configuration["ApiSettings:BaseUrl"];
    try
    {
        using var scope = app.Services.CreateScope();
        var diag = scope.ServiceProvider.GetRequiredService<DiagnosticoWebService>();
        var reachable = await diag.PingApiAsync();
        if (reachable)
            logger.LogInformation("API conectada com sucesso — BaseUrl: {BaseUrl}", baseUrl);
        else
            logger.LogWarning("API NÃO acessível no startup — BaseUrl: {BaseUrl}. Verifique a configuração.", baseUrl);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Falha ao verificar conectividade com API — BaseUrl: {BaseUrl}", baseUrl);
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/auth/login");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseHttpsRedirection();

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Remove("Server");
    context.Response.Headers.Remove("X-Powered-By");
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.UseSession();           // BEFORE Authentication
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.Run();
