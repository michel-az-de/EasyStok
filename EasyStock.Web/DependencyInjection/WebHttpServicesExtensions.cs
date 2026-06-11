using System.IO.Compression;
using System.Net.Http.Headers;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.ResponseCompression;

namespace EasyStock.Web.DependencyInjection;

/// <summary>
/// Consolida em uma só extension TODOS os ~37 registros DI que viviam soltos no Program.cs
/// do <c>EasyStock.Web</c>: Session (Redis ou InMemory), HttpContextAccessor,
/// SessionService + JwtClaimsReader + LucideIconResolver, 2 HttpClient&lt;T&gt; com
/// TokenRefreshHandler, 30+ domain Services (Scoped), Marketing options, Leads/Faq/Tickets
/// API services, ResponseCompression (Brotli/Gzip).
///
/// Lifetime de cada serviço preservado exatamente como estava no Program.cs original —
/// AddScoped continua Scoped, AddSingleton continua Singleton, AddHttpClient&lt;I,T&gt;
/// continua AddHttpClient.
/// </summary>
public static class WebHttpServicesExtensions
{
    public static IServiceCollection AddEasyStockWebHttpServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var config = builder.Configuration;

        // 2. Session — usa Redis se disponível (persistência entre deploys/réplicas), fallback in-memory
        var redisCs = config.GetConnectionString("Redis");
        if (!string.IsNullOrWhiteSpace(redisCs))
            services.AddStackExchangeRedisCache(o => o.Configuration = redisCs);
        else
            services.AddDistributedMemoryCache();

        services.AddSession(o =>
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
        services.AddHttpContextAccessor();

        // 4. Register SessionService first (used by TokenRefreshHandler)
        services.AddScoped<SessionService>();

        // 4a. Leitor de claims JWT — stateless, sem deps. Consumido por AuthController,
        // SessionRestoreMiddleware e TokenRefreshHandler para extrair sub/nivel/empresaId
        // do payload (sem validar assinatura — quem valida e a API).
        services.AddSingleton<IJwtClaimsReader, JwtClaimsReader>();

        // 4b. Design system: Lucide icon resolver (cache de SVGs em memoria, singleton)
        services.AddSingleton<LucideIconResolver>();

        // 5. HttpClient with TokenRefreshHandler
        services.AddScoped<TokenRefreshHandler>();
        services.AddHttpClient<ApiClient>(client =>
        {
            var baseUrl = config["ApiSettings:BaseUrl"]
                ?? throw new InvalidOperationException("ApiSettings:BaseUrl é obrigatório no appsettings.json");
            if (!baseUrl.EndsWith('/')) baseUrl += "/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(config.GetValue<int>("ApiSettings:TimeoutSeconds"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }).AddHttpMessageHandler<TokenRefreshHandler>();

        // 6. Domain services
        services.AddScoped<ProdutosService>();
        services.AddScoped<EstoqueService>();
        services.AddScoped<EntradasService>();
        services.AddScoped<SaidasService>();
        services.AddScoped<FornecedoresService>();
        services.AddScoped<ClientesService>();
        services.AddScoped<PedidosService>();
        services.AddScoped<CaixaService>();
        services.AddScoped<FinanceiroService>();
        services.AddScoped<LotesService>();
        services.AddScoped<EtiquetasService>();
        services.AddScoped<ListasComprasService>();
        services.AddScoped<AnalyticsService>();
        services.AddScoped<InteligenciaService>();
        services.AddScoped<NotificacoesService>();
        services.AddScoped<UsuariosService>();
        services.AddScoped<AssinaturaService>();
        services.AddScoped<AuditService>();
        services.AddScoped<ConfiguracoesService>();
        services.AddScoped<AnunciosService>();
        services.AddScoped<CategoriasService>();
        services.AddScoped<LojasService>();
        services.AddScoped<BuscaUnificadaService>();
        services.AddScoped<InteligenciaLojasService>();
        services.AddScoped<MobileDevicesService>();
        services.AddScoped<MobileProductsService>();
        services.AddScoped<OperacaoMobileService>();
        services.AddScoped<RelatoriosService>();
        services.AddScoped<NotasFiscaisService>();
        services.AddScoped<ConfiguracaoFiscalService>();

        // 6d. Menu lateral (ADR-0032, fatia 2): badges agregados com cache curto (60s)
        // por empresa+loja. AddMemoryCache registra IMemoryCache (distinto do distributed
        // cache da Session acima) — explícito mesmo que o Razor possa trazê-lo transitivo.
        services.AddMemoryCache();
        services.AddScoped<IMenuResumoSource, MenuResumoSource>();
        services.AddScoped<MenuResumoService>();

        // 6b. Marketing options + Leads API service (landing publica)
        services.Configure<MarketingOptions>(config.GetSection("Marketing"));
        services.AddScoped<LeadsApiService>();
        services.AddScoped<FaqApiService>();
        services.AddScoped<TicketsApiService>();

        // 6c. Response compression — Brotli/Gzip pra Razor HTML, JSON do AJAX e estaticos.
        // CPU overhead marginal vs ganho de bandwidth (Render cobra acima do free tier).
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
