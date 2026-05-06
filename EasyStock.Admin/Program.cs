using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ── Globalization ────────────────────────────────────────────────────────────
var ptBR = new System.Globalization.CultureInfo("pt-BR");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ptBR;
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ptBR;

// ── Services ─────────────────────────────────────────────────────────────────
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
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

builder.Services.AddTransient<AdminTokenRefreshHandler>();
builder.Services.AddHttpClient<AdminApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
}).AddHttpMessageHandler<AdminTokenRefreshHandler>();

// Session e API services
builder.Services.AddScoped<AdminSessionService>();

// Cookie auth (apenas para controlar acesso às pages via middleware)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
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

builder.Services.AddAuthorization();

var app = builder.Build();

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapRazorPages();

// Proxy endpoint para badges do sidebar (polling JS a cada 60s)
app.MapGet("/api-proxy/dashboard-badges", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>("api/admin/dashboard");
        int G(string k) => data.TryGetProperty(k, out var v)
            && v.ValueKind == System.Text.Json.JsonValueKind.Number
            && v.TryGetInt32(out var n) ? n : 0;
        decimal GD(string k) => data.TryGetProperty(k, out var v)
            && v.ValueKind == System.Text.Json.JsonValueKind.Number
            && v.TryGetDecimal(out var d) ? d : 0m;
        return Results.Ok(new
        {
            totalTenants              = G("totalTenants"),
            tenantsAtivos             = G("tenantsAtivos"),
            tenantsSuspensos          = G("tenantsSuspensos"),
            tenantsNovos              = G("tenantsNovosUltimos30Dias"),
            ticketsAbertos            = G("ticketsAbertos"),
            ticketsCriticos           = G("ticketsCriticos"),
            ticketsEmAtendimento      = G("ticketsEmAtendimento"),
            ticketsComNovaMensagem    = G("ticketsComNovaMensagem"),
            totalUsuariosAtivos       = G("totalUsuariosAtivos"),
            logins24h                 = G("logins24h"),
            receitaMensalEstimada     = GD("receitaMensalEstimada")
        });
    }
    catch (EasyStock.Admin.Services.SessionExpiredException)
    {
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        // Antes devolvia 200 OK com tudo zerado, mascarando outage como "estado real".
        // Agora 502 sinaliza falha pro JS do front exibir "atualizando…" em vez de "tudo zero".
        log.LogError(ex, "Proxy dashboard-badges: falha ao consultar API");
        return Results.Json(new { error = "upstream_unavailable" }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Proxy endpoint para Status Page (polling JS a cada 30s)
app.MapGet("/api-proxy/status", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>("api/admin/status");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException)
    {
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Proxy status: falha ao consultar API");
        return Results.Json(new { error = "upstream_unavailable" }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Proxy endpoint para exportação CSV de Audit Logs
app.MapGet("/api-proxy/audit-logs-csv", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
        var (bytes, ct) = await api.GetBytesAsync($"api/admin/audit-logs?{qs}");
        return Results.File(bytes, ct, "admin-audit-logs.csv");
    }
    catch (EasyStock.Admin.Services.SessionExpiredException)
    {
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Proxy audit-logs-csv: falha ao baixar CSV");
        return Results.Problem(
            title: "Erro ao gerar CSV",
            detail: "Não foi possível obter os logs de auditoria. Tente novamente em instantes.",
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
