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
});

// HTTP Client para API
var apiBaseUrl = builder.Configuration["ApiBaseUrl"]
    ?? throw new InvalidOperationException("ApiBaseUrl não configurado.");

builder.Services.AddHttpClient<AdminApiClient>(c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    c.Timeout = TimeSpan.FromSeconds(15);
});

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
    EasyStock.Admin.Services.AdminSessionService session) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>("api/admin/dashboard");
        return Results.Ok(new
        {
            totalTenants = data.TryGetProperty("totalTenants", out var tt) ? tt.GetInt32() : 0,
            ticketsCriticos = data.TryGetProperty("ticketsCriticos", out var tc) ? tc.GetInt32() : 0,
            ticketsAbertos = data.TryGetProperty("ticketsAbertos", out var ta) ? ta.GetInt32() : 0
        });
    }
    catch
    {
        return Results.Ok(new { totalTenants = 0, ticketsCriticos = 0, ticketsAbertos = 0 });
    }
});

app.Run();
