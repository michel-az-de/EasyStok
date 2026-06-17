using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;

namespace EasyStock.Admin.DependencyInjection;

/// <summary>
/// Agrupa todos os registros DI do <c>EasyStock.Admin</c> (Razor Pages, Session,
/// Cookie auth, HttpClient para a API, ResponseCompression).
///
/// Mantém a ordem relativa exata do Program.cs original. <c>ApiBaseUrl</c> é
/// obrigatório — exceção rola aqui pra fail-fast no startup.
/// </summary>
public static class AdminServicesExtensions
{
    public static IServiceCollection AddEasyStockAdminServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddRazorPages();
        // BUG-02 (#634): <input type=number> submete decimal com ponto, mas o Admin roda em
        // pt-BR e o binder padrão lança em "19.90" -> decimal cai em 0 e o save corrompe/bloqueia.
        // Liga decimal/double/float com InvariantCulture (cobre criação e edição de todos os forms).
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(options =>
            options.ModelBinderProviders.Insert(0, new ModelBinding.InvariantDecimalModelBinderProvider()));
        services.AddHttpContextAccessor();

        // Session
        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            var timeoutMinutes = builder.Configuration.GetValue<int>("Session:TimeoutMinutes", 480);
            var cookieName = builder.Configuration["Session:CookieName"] ?? ".EasyStock.Admin";
            options.IdleTimeout = TimeSpan.FromMinutes(timeoutMinutes);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.Name = cookieName;
            options.Cookie.SameSite = SameSiteMode.Strict;
            // Em prod sempre HTTPS; em dev permite HTTP local.
            options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
        });

        // HTTP Client para API
        var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
            ?? throw new InvalidOperationException("ApiBaseUrl não configurado.");

        services.AddTransient<AdminTokenRefreshHandler>();
        services.AddHttpClient<AdminApiClient>(c =>
        {
            c.BaseAddress = new Uri(apiBaseUrl);
            c.Timeout = TimeSpan.FromSeconds(15);
        }).AddHttpMessageHandler<AdminTokenRefreshHandler>();

        // Session e API services
        services.AddScoped<AdminSessionService>();

        // Cookie auth (apenas para controlar acesso às pages via middleware)
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Auth/Login";
                options.LogoutPath = "/Auth/Logout";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(480);
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
            });

        services.AddAuthorization();

        // DataProtection: persiste as chaves num volume em producao (mesma motivacao do
        // EasyStock.Web). Sem isso as chaves de assinatura do cookie de sessao + antiforgery
        // sao regeneradas a cada restart. KeysPath vem do ambiente (volume montado); em dev
        // (sem KeysPath) cai no default efemero. Guard de gravabilidade evita 500 se o volume
        // nascer root-owned (container roda como appuser uid 1001). A persistencia da SESSAO
        // em si (token) e resolvida pelo AdminSessionRestoreMiddleware (cookie _rt_admin).
        var dpKeysPath = builder.Configuration["DataProtection:KeysPath"];
        var dp = services.AddDataProtection().SetApplicationName("EasyStok.Admin");
        if (!string.IsNullOrWhiteSpace(dpKeysPath))
        {
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
                Console.Error.WriteLine($"[DataProtection] AVISO: '{dpKeysPath}' nao gravavel ({ex.GetType().Name}) — chaves ficarao efemeras. Verifique o volume/permissao (chown 1001).");
            }
            if (dpWritable)
            {
                dp.PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath));
                Console.WriteLine($"[DataProtection] Persistindo chaves em '{dpKeysPath}'.");
            }
        }

        // Response compression — Brotli/Gzip pra Razor Pages + JSON dos /api-proxy/*.
        // Render cobra bandwidth; CPU overhead marginal.
        services.AddResponseCompression(o =>
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
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

        return services;
    }
}
