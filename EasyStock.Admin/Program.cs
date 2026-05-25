using System.IO.Compression;
using System.Text.Json;
using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.ResponseCompression;

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

// Response compression — Brotli/Gzip pra Razor Pages + JSON dos /api-proxy/*.
// Render cobra bandwidth; CPU overhead marginal.
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

var app = builder.Build();

// ForwardedHeaders: Fly/Render/etc fazem TLS no edge e mandam HTTP com
// X-Forwarded-Proto=https. Sem isso o UseHttpsRedirection estoura 400.
app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
                     | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedHost,
    KnownNetworks = { },
    KnownProxies = { }
});

// ── Middleware ────────────────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseResponseCompression();
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
        var data = await api.GetAsync<JsonElement>("api/admin/dashboard");
        int G(string k) => data.TryGetProperty(k, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var n) ? n : 0;
        decimal GD(string k) => data.TryGetProperty(k, out var v)
            && v.ValueKind == JsonValueKind.Number
            && v.TryGetDecimal(out var d) ? d : 0m;
        return Results.Ok(new
        {
            totalTenants = G("totalTenants"),
            tenantsAtivos = G("tenantsAtivos"),
            tenantsSuspensos = G("tenantsSuspensos"),
            tenantsNovos = G("tenantsNovosUltimos30Dias"),
            ticketsAbertos = G("ticketsAbertos"),
            ticketsCriticos = G("ticketsCriticos"),
            ticketsEmAtendimento = G("ticketsEmAtendimento"),
            ticketsComNovaMensagem = G("ticketsComNovaMensagem"),
            totalUsuariosAtivos = G("totalUsuariosAtivos"),
            logins24h = G("logins24h"),
            receitaMensalEstimada = GD("receitaMensalEstimada")
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
        var data = await api.GetAsync<JsonElement>("api/admin/status");
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
        var data = await api.GetAsync<JsonElement>($"api/admin/buscar-global?{qs}");
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
        using var doc = await JsonDocument.ParseAsync(ctx.Request.Body);
        if (doc.RootElement.TryGetProperty("motivo", out var m) && m.ValueKind == JsonValueKind.String)
            motivo = m.GetString();
    }
    catch { /* corpo invalido — vai cair na validacao do backend */ }

    if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 10)
        return Results.BadRequest(new { error = "motivo deve ter no mínimo 10 caracteres." });

    try
    {
        var data = await api.PostAsync<JsonElement>(
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
        var data = await api.GetAsync<JsonElement>($"api/diagnostico/logs/enhanced?hours={Uri.EscapeDataString(hours)}");
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
        var data = await api.GetAsync<JsonElement>($"api/diagnostico/logs/search?{qs}");
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
        var data = await api.PostAsync<JsonElement>($"api/admin/seed/run-async?{qs}", new { });
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
        var data = await api.GetAsync<JsonElement>($"api/admin/seed/run/{runId}");
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
        var data = await api.GetAsync<JsonElement>($"api/admin/seed/runs?{qs}");
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

// ── Proxies SystemErrorLog + DiagnosticoMode ──────────────────────────────────

// Recebe erros de frontend e repassa para a API.
app.MapPost("/api-proxy/diag/frontend-error", async (
    EasyStock.Admin.Services.AdminApiClient api,
    HttpRequest req,
    ILogger<Program> log) =>
{
    try
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        await api.PostRawAsync("api/diagnostico/frontend-error", payload);
        return Results.Ok(new { ok = true });
    }
    catch (Exception ex)
    {
        log.LogDebug(ex, "Proxy diag/frontend-error falhou (não crítico)");
        return Results.Ok(new { ok = false }); // nunca 5xx — não quebra o UI
    }
});

// Lista erros do banco (SystemErrorLog).
app.MapGet("/api-proxy/diag/system-errors", async (
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
        var data = await api.GetAsync<JsonElement>($"api/diagnostico/system-errors?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/system-errors falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Purgar erros do banco.
app.MapPost("/api-proxy/diag/system-errors/expurgar", async (
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
        var data = await api.PostAsync<JsonElement>($"api/diagnostico/system-errors/expurgar?{qs}", new { });
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/system-errors/expurgar falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Lê estado atual do logging mode.
app.MapGet("/api-proxy/diag/logging-mode", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<JsonElement>("api/diagnostico/logging-mode");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/logging-mode GET falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Altera logging mode.
app.MapPost("/api-proxy/diag/logging-mode", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var data = await api.PostAsync<JsonElement>("api/diagnostico/logging-mode", payload);
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/logging-mode POST falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Snapshot completo de infra (banco, redis, smtp, storage, ia, config).
app.MapGet("/api-proxy/diag/infra", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>("api/diagnostico");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/infra falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Health de cada endpoint-chave (latência, status, timeout).
app.MapGet("/api-proxy/diag/endpoints", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>("api/diagnostico/endpoints");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/endpoints falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// SLO — uptime 24h, avg/p95 response time, error rate (pass-through de ?hours=).
app.MapGet("/api-proxy/diag/slo", async (
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
        var data = await api.GetAsync<System.Text.Json.JsonElement>($"api/diagnostico/slo?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/slo falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Queries lentas do PostgreSQL via pg_stat_statements.
app.MapGet("/api-proxy/diag/queries-lentas", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken()))
        return Results.Unauthorized();
    try
    {
        var data = await api.GetAsync<System.Text.Json.JsonElement>("api/diagnostico/queries-lentas");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/queries-lentas falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// ──────────────────────────────────────────────────────────────────────
// Proxies /api-proxy/mobile/* — alimentam as páginas /Operacao e /Dispositivos
// (gestão de devices PWA pareados, dashboard live, comandos remotos OTA).
// SuperAdmin pode operar em qualquer empresa; Admin de empresa só na própria
// (regra aplicada já no backend via ICurrentUserAccessor).
// ──────────────────────────────────────────────────────────────────────

// Dashboard live de operação (KPIs do dia da empresa/loja).
app.MapGet("/api-proxy/mobile/operacao/dashboard", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
        var data = await api.GetJsonAsync<JsonElement>($"api/mobile/operation/dashboard?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/operacao/dashboard falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Onda 1.3 — proxy do diagnostico de email (envia teste pelo provedor ativo).
app.MapPost("/api-proxy/diag/email-teste", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = await api.PostJsonAsync<System.Text.Json.JsonElement>("api/admin/diagnostico/email/teste", body);
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/email-teste falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Onda 2.1 — proxy do diagnostico de WhatsApp (envia texto ou template via Meta Cloud).
app.MapPost("/api-proxy/diag/whatsapp-teste", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var body = await ctx.Request.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var data = await api.PostJsonAsync<System.Text.Json.JsonElement>("api/admin/diagnostico/whatsapp/teste", body);
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy diag/whatsapp-teste falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Onda 1.4 — proxy do resumo de tickets criticos.
// - Sem empresaId: cross-tenant (badge global no _Layout admin)
// - Com empresaId: por empresa (widget na pagina Operacao)
app.MapGet("/api-proxy/admin/tickets/criticos-resumo", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
        var data = await api.GetJsonAsync<System.Text.Json.JsonElement>($"api/admin/tickets/criticos-resumo?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy admin/tickets/criticos-resumo falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Saúde dos devices da empresa (badge ok/warn/err + último visto).
app.MapGet("/api-proxy/mobile/operacao/devices-health", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
        var data = await api.GetJsonAsync<JsonElement>($"api/mobile/operation/devices-health?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/operacao/devices-health falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Listagem de devices pareados (sumarização básica).
app.MapGet("/api-proxy/mobile/devices", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
        var data = await api.GetJsonAsync<JsonElement>($"api/mobile/devices?{qs}");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/devices falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Gera código de pareamento (6 dígitos válidos por 10 min).
app.MapPost("/api-proxy/mobile/devices/pair-codes", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var data = await api.PostJsonAsync<JsonElement>("api/mobile/devices/pair-codes", payload);
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/devices/pair-codes falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Enfileira comando remoto pra um device (flush_now, pull_now, reload, message,
// pwa_update, clear_cache).
app.MapPost("/api-proxy/mobile/devices/{id}/commands", async (
    string id,
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var data = await api.PostJsonAsync<JsonElement>(
            $"api/mobile/devices/{Uri.EscapeDataString(id)}/commands", payload);
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/devices/{Id}/commands falhou", id);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Broadcast: enfileira mesmo comando pra todos os devices da empresa/loja.
// Use case primário: gestor força "atualização pelo web" (commandType=pwa_update)
// pra todos os PWAs ativos de uma vez.
app.MapPost("/api-proxy/mobile/devices/broadcast", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpContext ctx,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        using var reader = new StreamReader(ctx.Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);
        var data = await api.PostJsonAsync<JsonElement>(
            "api/mobile/devices/broadcast", payload);
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/devices/broadcast falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Revoga device pareado (DELETE).
app.MapDelete("/api-proxy/mobile/devices/{id}", async (
    string id,
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        await api.DeleteAsync($"api/mobile/devices/{Uri.EscapeDataString(id)}");
        return Results.NoContent();
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/devices/{Id} (DELETE) falhou", id);
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// Versão atual reportada pelo backend (pra mostrar no Admin qual CACHE_VERSION
// o servidor está servindo).
app.MapGet("/api-proxy/mobile/version", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        var data = await api.GetJsonAsync<JsonElement>("api/mobile/version");
        return Results.Ok(data);
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy mobile/version falhou");
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
    }
});

// ── Proxy /api-proxy/notif/preview-draft ────────────────────────────────────
// Editor de Template (Admin) chama isto pra preview ao vivo (debounce 400ms).
app.MapPost("/api-proxy/notif/preview-draft", async (
    EasyStock.Admin.Services.AdminApiClient api,
    EasyStock.Admin.Services.AdminSessionService session,
    HttpRequest req,
    ILogger<Program> log) =>
{
    if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
    try
    {
        using var reader = new System.IO.StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();
        var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
        var data = await api.PostRawAsync("api/admin/notificacoes/templates/preview-draft", payload);
        return Results.Content(data.GetRawText(), "application/json");
    }
    catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Proxy notif/preview-draft falhou");
        return Results.Json(new { error = new { message = ex.Message } }, statusCode: StatusCodes.Status502BadGateway);
    }
});

app.Run();
