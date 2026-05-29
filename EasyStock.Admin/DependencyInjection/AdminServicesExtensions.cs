using System.IO.Compression;
using Microsoft.AspNetCore.Authentication.Cookies;
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
