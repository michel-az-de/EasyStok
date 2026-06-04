using System.Globalization;
using EasyStock.Web.DependencyInjection;
using EasyStock.Web.Infrastructure;
using EasyStock.Web.Middleware;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

// 1. Cultura pt-BR global
var ptBR = new CultureInfo("pt-BR");
CultureInfo.DefaultThreadCurrentCulture = ptBR;
CultureInfo.DefaultThreadCurrentUICulture = ptBR;

// === Web services + HttpClients + Compression (em DependencyInjection/WebHttpServicesExtensions.cs) ===
builder.AddEasyStockWebHttpServices();

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

        // BUG-65 (#452): chamadas AJAX (X-Requested-With/Accept: application/json) recebem
        // 401/403 JSON em vez do 302 -> página de login HTML (que o fetch seguiria e
        // quebraria em r.json()). Navegação de documento continua redirecionando.
        o.Events.OnRedirectToLogin = ctx =>
        {
            if (AjaxRequest.WantsJson(ctx.Request))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
        o.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (AjaxRequest.WantsJson(ctx.Request))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            ctx.Response.Redirect(ctx.RedirectUri);
            return Task.CompletedTask;
        };
    });

// 8b. DataProtection — persistir as chaves num volume em producao.
// Sem isso as chaves ficam in-memory: todo restart/deploy as regenera, o cookie de
// auth deixa de descriptografar, o SessionRestoreMiddleware ve IsAuthenticated=false
// e nem chega a restaurar via _rt => logout geral a cada deploy (BUG-004 + cascata
// 005/006/007/008). Com DataProtection:KeysPath apontando pra um volume montado, o
// cookie sobrevive ao deploy e o "manter logado" funciona como projetado. Em dev
// (sem KeysPath) cai no default efemero — sem impacto local.
var dpKeysPath = config["DataProtection:KeysPath"];
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("EasyStok.Web");
if (!string.IsNullOrWhiteSpace(dpKeysPath))
{
    // Guard: so persiste se o diretorio for de fato gravavel. O container roda como
    // appuser (uid 1001); um volume root-owned daria 500 no 1o uso do cookie. Se nao
    // der pra gravar, cai no default efemero (comportamento atual) e loga o aviso —
    // degrada a sessao, mas NAO quebra a autenticacao.
    var dpWritable = false;
    try
    {
        Directory.CreateDirectory(dpKeysPath);
        var probe = Path.Combine(dpKeysPath, ".write-probe");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
        dpWritable = true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[DataProtection] AVISO: '{dpKeysPath}' nao gravavel ({ex.GetType().Name}) — chaves ficarao efemeras (sessao nao sobrevive a deploy). Verifique o volume/permissao (chown 1001).");
    }
    if (dpWritable)
    {
        dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
        Console.WriteLine($"[DataProtection] Persistindo chaves em '{dpKeysPath}'.");
    }
}

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
        // Ping leve e anonimo na API (health/live) — antes usava DiagnosticoWebService,
        // removido junto com a UI /diagnostico da Web (centralizada no Admin).
        var httpFactory = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        var pingClient = httpFactory.CreateClient();
        pingClient.Timeout = TimeSpan.FromSeconds(10);
        var reachable = false;
        try
        {
            var pingBase = (baseUrl ?? "").TrimEnd('/');
            var pingResp = await pingClient.GetAsync($"{pingBase}/health/live");
            reachable = pingResp.IsSuccessStatusCode;
        }
        catch { reachable = false; }
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

// Health check — exigido pelo Dockerfile.Web e pelo Azure App Service.
// Expõe version (da assembly, origem única do csproj) + commit (ARG GIT_SHA do build)
// para rastreabilidade: qual commit está no ar em 1 request, sem auditar às cegas (#453).
var healthVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
var healthCommit = Environment.GetEnvironmentVariable("GIT_SHA");
app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    version = healthVersion is not null
        ? $"{healthVersion.Major}.{healthVersion.Minor}.{healthVersion.Build}"
        : "unknown",
    commit = string.IsNullOrWhiteSpace(healthCommit) ? "unknown" : healthCommit
}))
   .AllowAnonymous()
   .ExcludeFromDescription();

app.Run();

namespace EasyStock.Web
{
    /// <summary>
    /// Marcador de assembly para <c>WebApplicationFactory</c> nos testes de pipeline
    /// (#452). O <c>Program</c> dos top-level statements é global e colide com o
    /// <c>Program</c> do EasyStock.Admin no projeto de testes (que referencia ambos);
    /// este marcador namespaced resolve a ambiguidade.
    /// </summary>
    public sealed class WebTestEntryPoint { }
}
