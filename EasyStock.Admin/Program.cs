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

// Aliases /Clientes → /Tenants (sidebar label foi renomeada na slice de Gestão de Cliente,
// mas as rotas internas seguem `/Tenants`. Redirect mantém URLs digitadas funcionando).
app.MapGet("/Clientes", () => Results.Redirect("/Tenants", permanent: false));
app.MapGet("/Clientes/Detail/{id:guid}", (Guid id) => Results.Redirect($"/Tenants/Detail/{id}", permanent: false));

// /Status absorvido em /Diagnostico (slice "Diagnóstico de Erros + Seed Visível").
// Redirect 301 mantém bookmarks/links externos funcionando. Remover daqui a 1-2 releases.
app.MapGet("/Status", () => Results.Redirect("/Diagnostico", permanent: true));

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

// Proxy busca global (Cmd+K). Debounce client-side de 200ms; clamp limit no backend.
app.MapGet("/api-proxy/buscar-global", async (
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
        var data = await api.GetAsync<System.Text.Json.JsonElement>($"api/admin/buscar-global?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException)
    {
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Proxy buscar-global: falha");
        return Results.Json(new { error = "upstream_unavailable" }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Proxy revelar dados de cliente (LGPD). POST com motivo no body — o motivo
// vai para AdminAuditLog. Usado pelo modal "Revelar dados completos" no detalhe
// do ticket (Pages/Tickets/Detail.cshtml).
app.MapPost("/api-proxy/admin-empresas-revelar", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();

    var empresaIdStr = ctx.Request.Query["empresaId"].FirstOrDefault();
    if (!Guid.TryParse(empresaIdStr, out var empresaId))
        return Results.BadRequest(new { error = "empresaId inválido." });

    Guid? ticketId = null;
    if (Guid.TryParse(ctx.Request.Query["ticketId"].FirstOrDefault(), out var tid))
        ticketId = tid;

    string? motivo = null;
    try
    {
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(ctx.Request.Body);
        if (doc.RootElement.TryGetProperty("motivo", out var m) && m.ValueKind == System.Text.Json.JsonValueKind.String)
            motivo = m.GetString();
    }
    catch { /* corpo invalido — vai cair na validacao do backend */ }

    if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 10)
        return Results.BadRequest(new { error = "motivo deve ter no mínimo 10 caracteres." });

    try
    {
        var data = await api.PostAsync<System.Text.Json.JsonElement>(
            $"api/admin/empresas/{empresaId}/preview/revelar",
            new { motivo, ticketIdContexto = ticketId });
        return Results.Ok(new { data });
    }
    catch (EasyStock.Admin.Services.SessionExpiredException)
    {
        return Results.Unauthorized();
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Proxy admin-empresas-revelar: falha");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// ──────────────────────────────────────────────────────────────────────
// Proxies /api-proxy/diag/* — alimentam a tela /Diagnostico do Admin.
// Mantemos a sessão cookie no Admin e injetamos o Bearer no AdminApiClient.
// ──────────────────────────────────────────────────────────────────────

// Header counters (última hora + 24h) — usa /diagnostico/logs/enhanced.
app.MapGet("/api-proxy/diag/summary", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var hours = ctx.Request.Query["hours"].FirstOrDefault() ?? "24";
        var data = await api.GetAsync<System.Text.Json.JsonElement>($"api/diagnostico/logs/enhanced?hours={Uri.EscapeDataString(hours)}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/summary falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Listagem paginada+filtrada — núcleo da tab Erros.
app.MapGet("/api-proxy/diag/search", async (
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
        var data = await api.GetAsync<System.Text.Json.JsonElement>($"api/diagnostico/logs/search?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/search falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// ── Seed async (progresso em tempo real) ──────────────────────────────────────

// Inicia run em background e retorna runId imediatamente.
app.MapPost("/api-proxy/seed/run-async", async (
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
        var data = await api.PostAsync<System.Text.Json.JsonElement>($"api/admin/seed/run-async?{qs}", new { });
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy seed/run-async falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Polling de status de um run específico.
app.MapGet("/api-proxy/seed/run/{runId:guid}", async (
    Guid runId,
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>($"api/admin/seed/run/{runId}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy seed/run/{RunId} falhou", runId);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Histórico de runs (auditoria).
app.MapGet("/api-proxy/seed/runs", async (
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
        var data = await api.GetAsync<System.Text.Json.JsonElement>($"api/admin/seed/runs?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy seed/runs falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Export JSON (binário passthrough) — alimenta o botão "Exportar JSON".
// Não consegue usar GetAsync<JsonElement> porque o endpoint devolve File(); usa GetBytesAsync.
app.MapGet("/api-proxy/diag/export", async (
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
        var (bytes, ct) = await api.GetBytesAsync($"api/diagnostico/logs/exportar?{qs}");
        var fileName = $"easystock-logs-{DateTime.UtcNow:yyyyMMdd-HHmm}.json";
        return Results.File(bytes, ct, fileName);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/export falhou");
        return Results.Problem(
            title: "Erro ao exportar logs",
            detail: ex.Message,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
