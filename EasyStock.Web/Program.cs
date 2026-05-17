using System.Globalization;
using System.IO.Compression;
using System.Net.Http.Headers;
using EasyStock.Web.Infrastructure;
using EasyStock.Web.Middleware;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// 1. Cultura pt-BR global
var ptBR = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = ptBR;
CultureInfo.DefaultThreadCurrentUICulture = ptBR;

// 2. Session — usa Redis se disponível (persistência entre deploys/réplicas), fallback in-memory
var redisCs = config.GetConnectionString("Redis");
if (!string.IsNullOrWhiteSpace(redisCs))
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisCs);
else
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

// 4b. Design system: Lucide icon resolver (cache de SVGs em memoria, singleton)
builder.Services.AddSingleton<LucideIconResolver>();

// 5. HttpClient with TokenRefreshHandler
builder.Services.AddScoped<TokenRefreshHandler>();
builder.Services.AddHttpClient<ApiClient>(client =>
{
    var baseUrl = config["ApiSettings:BaseUrl"]
        ?? throw new InvalidOperationException("ApiSettings:BaseUrl é obrigatório no appsettings.json");
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(config.GetValue<int>("ApiSettings:TimeoutSeconds"));
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
}).AddHttpMessageHandler<TokenRefreshHandler>();

// 5b. HttpClient for diagnostics — agora exige auth (controllers da API DiagnosticoController/InfraController/LogsController
// estao com [Authorize(Policy="Admin")] no nivel da classe). Sem o handler, todas as chamadas voltam 401
// e a aba Endpoints / o card principal renderizam vazio mesmo pro SuperAdmin logado.
builder.Services.AddHttpClient<DiagnosticoWebService>(client =>
{
    var baseUrl = config["ApiSettings:BaseUrl"]!;
    // Ensure base URL ends with /
    if (!baseUrl.EndsWith('/')) baseUrl += "/";
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(10);
}).AddHttpMessageHandler<TokenRefreshHandler>();

// 6. Domain services
builder.Services.AddScoped<ProdutosService>();
builder.Services.AddScoped<EstoqueService>();
builder.Services.AddScoped<EntradasService>();
builder.Services.AddScoped<SaidasService>();
builder.Services.AddScoped<FornecedoresService>();
builder.Services.AddScoped<ClientesService>();
builder.Services.AddScoped<PedidosService>();
builder.Services.AddScoped<CaixaService>();
builder.Services.AddScoped<LotesService>();
builder.Services.AddScoped<EtiquetasService>();
builder.Services.AddScoped<ListasComprasService>();
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
builder.Services.AddScoped<MobileDevicesService>();
builder.Services.AddScoped<MobileProductsService>();
builder.Services.AddScoped<OperacaoMobileService>();
builder.Services.AddScoped<RelatoriosService>();

// 6b. Marketing options + Leads API service (landing publica)
builder.Services.Configure<MarketingOptions>(config.GetSection("Marketing"));
builder.Services.AddScoped<LeadsApiService>();
builder.Services.AddScoped<FaqApiService>();
builder.Services.AddScoped<TicketsApiService>();

// 6c. Response compression — Brotli/Gzip pra Razor HTML, JSON do AJAX e estaticos.
// CPU overhead marginal vs ganho de bandwidth (Render cobra acima do free tier).
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

// 7. MVC + Antiforgery automático
builder.Services.AddControllersWithViews(o =>
{
    o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    // Decimal binder com InvariantCulture — evita que "8.5" (JS) seja interpretado
    // como 85 pelo model binder padrão em cultura pt-BR.
    o.ModelBinderProviders.Insert(0, new InvariantDecimalModelBinderProvider());
});

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

// ForwardedHeaders: Fly/Render/etc fazem TLS termination no edge e mandam
// HTTP pra container com X-Forwarded-Proto=https. Sem isso o UseHttpsRedirection
// vê HTTP, tenta redirect, e estoura 400 (não sabe qual porta HTTPS).
app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

// Startup: verificar conectividade com a API (não-bloqueante).
// Tudo envolto em try/catch externo porque falhas em GetRequiredService
// (antes de entrar no try interno) senão seriam exceções não observadas em Task.Run.
_ = Task.Run(async () =>
{
    ILogger? logger = null;
    var baseUrl = app.Configuration["ApiSettings:BaseUrl"];
    try
    {
        logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
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
        // Se o logger já foi criado usa ele; senão imprime em stderr para não perder o traço
        if (logger is not null)
            logger.LogWarning(ex, "Falha ao verificar conectividade com API — BaseUrl: {BaseUrl}", baseUrl);
        else
            Console.Error.WriteLine($"[Startup] Falha ao verificar conectividade com API: {ex}");
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error/500");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/error/{0}");

// ResponseCompression precisa rodar antes de StaticFiles pra comprimir CSS/JS/SVG.
app.UseResponseCompression();
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

// .apk precisa do MIME correto para o Chrome/Edge baixar como arquivo
// (e não tentar abrir como octet-stream genérico). Aplicado aqui pra
// /downloads/easystok-*.apk servidos diretamente pelo middleware estático.
var contentTypes = new FileExtensionContentTypeProvider();
contentTypes.Mappings[".apk"] = "application/vnd.android.package-archive";
app.UseStaticFiles(new StaticFileOptions { ContentTypeProvider = contentTypes });

app.UseRouting();
app.UseSession();           // BEFORE Authentication
app.UseAuthentication();
app.UseMiddleware<SessionRestoreMiddleware>(); // Restaura sessão do _rt cookie após deploys
app.UseAuthorization();
app.UseMiddleware<LojaRequiredMiddleware>(); // Bloqueia navegacao sem LojaId selecionada

// Atalhos de URL: rotas canônicas que o menu/sidebar usa mas que usuários
// podem tentar acessar diretamente pelo alias mais curto.
app.MapGet("/compras", () => Results.Redirect("/listas-compras", permanent: false))
   .RequireAuthorization();
app.MapGet("/lista-de-compras", () => Results.Redirect("/listas-compras", permanent: true))
   .RequireAuthorization();
app.MapGet("/entradas", () => Results.Redirect("/entradas/historico", permanent: false))
   .RequireAuthorization();

// Roteamento — landing publica e raiz; Dashboard e o resto via path explicito.
// Quando os dois dominios forem ativados (easystok.com.br + app.easystok.com.br),
// adicionar middleware/RequireHost aqui para separar publico de autenticado.
// SiteController tem [Route("/")] e redireciona pra Dashboard se ja autenticado.
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Site}/{action=Index}/{id?}");

// Health check — exigido pelo Dockerfile.Web e pelo Azure App Service
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }))
   .AllowAnonymous()
   .ExcludeFromDescription();

app.Run();
