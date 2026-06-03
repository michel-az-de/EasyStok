namespace EasyStock.Admin.Hosting;

/// <summary>
/// 34 proxies <c>/api-proxy/*</c> que o Admin expõe pro front:
/// dashboard-badges, status, audit-logs-csv, buscar-global, admin-empresas-revelar,
/// diag/* (summary, search, export, frontend-error, system-errors, logging-mode, infra,
/// endpoints, slo, queries-lentas, email-teste, whatsapp-teste), seed/* (run-async, run, runs),
/// mobile/* (operacao/*, devices, devices/pair-codes, devices/{id}/commands, devices/broadcast,
/// version), admin/tickets/criticos-resumo, notif/preview-draft.
///
/// Cada proxy injeta Bearer da sessão Admin no AdminApiClient e devolve 401/502 conforme
/// estado do upstream. Transcrição verbatim do que vivia no Program.cs — divergências
/// devem ser commit separado deliberado.
/// </summary>
public static class ApiProxyEndpoints
{
    public static void MapAdminApiProxies(this WebApplication app)
    {
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
                // Agora 502 sinaliza falha pro JS do front exibir "atualizandoâ€¦" em vez de "tudo zero".
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

        // Proxy endpoint para exportaÃ§Ã£o CSV de Audit Logs
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
                    detail: "NÃ£o foi possÃ­vel obter os logs de auditoria. Tente novamente em instantes.",
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

        // Proxy revelar dados de cliente (LGPD). POST com motivo no body â€” o motivo
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
                return Results.BadRequest(new { error = "empresaId invÃ¡lido." });

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
            catch { /* corpo invalido â€” vai cair na validacao do backend */ }

            if (string.IsNullOrWhiteSpace(motivo) || motivo.Length < 10)
                return Results.BadRequest(new { error = "motivo deve ter no mÃ­nimo 10 caracteres." });

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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Proxies /api-proxy/diag/* â€” alimentam a tela /Diagnostico do Admin.
        // Mantemos a sessÃ£o cookie no Admin e injetamos o Bearer no AdminApiClient.
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // Header counters (Ãºltima hora + 24h) â€” usa /diagnostico/logs/enhanced.
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

        // Listagem paginada+filtrada â€” nÃºcleo da tab Erros.
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

        // â”€â”€ Seed async (progresso em tempo real) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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

        // Polling de status de um run especÃ­fico.
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

        // HistÃ³rico de runs (auditoria).
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

        // Export JSON (binÃ¡rio passthrough) â€” alimenta o botÃ£o "Exportar JSON".
        // NÃ£o consegue usar GetAsync<JsonElement> porque o endpoint devolve File(); usa GetBytesAsync.
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

        // â”€â”€ Proxies SystemErrorLog + DiagnosticoMode â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

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
                log.LogDebug(ex, "Proxy diag/frontend-error falhou (nÃ£o crÃ­tico)");
                return Results.Ok(new { ok = false }); // nunca 5xx â€” nÃ£o quebra o UI
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

        // LÃª estado atual do logging mode.
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

        // Health de cada endpoint-chave (latÃªncia, status, timeout).
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

        // SLO â€” uptime 24h, avg/p95 response time, error rate (pass-through de ?hours=).
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

        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        // Proxies /api-proxy/mobile/* â€” alimentam as pÃ¡ginas /Operacao e /Dispositivos
        // (gestÃ£o de devices PWA pareados, dashboard live, comandos remotos OTA).
        // SuperAdmin pode operar em qualquer empresa; Admin de empresa sÃ³ na prÃ³pria
        // (regra aplicada jÃ¡ no backend via ICurrentUserAccessor).
        // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        // Dashboard live de operaÃ§Ã£o (KPIs do dia da empresa/loja).
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

        // Onda 1.3 â€” proxy do diagnostico de email (envia teste pelo provedor ativo).
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

        // Onda 2.1 â€” proxy do diagnostico de WhatsApp (envia texto ou template via Meta Cloud).
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

        // Onda 1.4 â€” proxy do resumo de tickets criticos.
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

        // SaÃºde dos devices da empresa (badge ok/warn/err + Ãºltimo visto).
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

        // Listagem de devices pareados (sumarizaÃ§Ã£o bÃ¡sica).
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

        // Gera cÃ³digo de pareamento (6 dÃ­gitos vÃ¡lidos por 10 min).
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
        // Use case primÃ¡rio: gestor forÃ§a "atualizaÃ§Ã£o pelo web" (commandType=pwa_update)
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

        // VersÃ£o atual reportada pelo backend (pra mostrar no Admin qual CACHE_VERSION
        // o servidor estÃ¡ servindo).
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

        // â”€â”€ Proxy /api-proxy/notif/preview-draft â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
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

        // ────────────────────────────────────────────────────────────────────────
        // Onda 1 (#431) — proxies diag/* de paridade com a antiga UI da Web.
        // Alimentam os charts (historico/eventos), o painel de padrões/alertas
        // (alertas/ack + alertas/acks) e o card de saúde por empresa do /Diagnostico
        // do Admin. Todos batem em DiagnosticoInfraController da API (Policy=Admin).
        // ────────────────────────────────────────────────────────────────────────

        // Histórico de health snapshots (~2h) — alimenta o chart Health Timeline.
        app.MapGet("/api-proxy/diag/historico", async (
            EasyStock.Admin.Services.AdminApiClient api,
            EasyStock.Admin.Services.AdminSessionService session,
            ILogger<Program> log) =>
        {
            if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
            try
            {
                var data = await api.GetAsync<JsonElement>("api/diagnostico/historico");
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/historico falhou");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Zera o histórico de snapshots (linha de base limpa após deploy/correção).
        app.MapPost("/api-proxy/diag/historico/zerar", async (
            EasyStock.Admin.Services.AdminApiClient api,
            EasyStock.Admin.Services.AdminSessionService session,
            ILogger<Program> log) =>
        {
            if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
            try
            {
                var data = await api.PostAsync<JsonElement>("api/diagnostico/historico/zerar", new { });
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/historico/zerar falhou");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Timeline de eventos (error spikes, deploys) — overlay do Health Timeline.
        app.MapGet("/api-proxy/diag/eventos", async (
            EasyStock.Admin.Services.AdminApiClient api,
            EasyStock.Admin.Services.AdminSessionService session,
            HttpContext ctx,
            ILogger<Program> log) =>
        {
            if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
            try
            {
                var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
                var data = await api.GetAsync<JsonElement>($"api/diagnostico/eventos?{qs}");
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/eventos falhou");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Marca/atualiza o ack de um padrão/alerta (visto, em_investigacao, resolvido).
        app.MapPost("/api-proxy/diag/alertas/{alertaId}/ack", async (
            string alertaId,
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
                var data = await api.PostAsync<JsonElement>(
                    $"api/diagnostico/alertas/{Uri.EscapeDataString(alertaId)}/ack", payload);
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/alertas/{AlertaId}/ack falhou", alertaId);
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Lê os acks atuais de uma lista de alertaIds (?ids=a,b,c).
        app.MapGet("/api-proxy/diag/alertas/acks", async (
            EasyStock.Admin.Services.AdminApiClient api,
            EasyStock.Admin.Services.AdminSessionService session,
            HttpContext ctx,
            ILogger<Program> log) =>
        {
            if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
            try
            {
                var qs = ctx.Request.QueryString.Value?.TrimStart('?') ?? "";
                var data = await api.GetAsync<JsonElement>($"api/diagnostico/alertas/acks?{qs}");
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/alertas/acks falhou");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Saúde por empresa (latência de queries por tenant) — card do /Diagnostico.
        app.MapGet("/api-proxy/diag/health-empresas", async (
            EasyStock.Admin.Services.AdminApiClient api,
            EasyStock.Admin.Services.AdminSessionService session,
            ILogger<Program> log) =>
        {
            if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
            try
            {
                var data = await api.GetAsync<JsonElement>("api/diagnostico/health/empresas");
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/health-empresas falhou");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // Onda 2 (#438) — health consolidado da stack (Postgres, Redis, Config, Dispatcher/
        // notificacoes). Alimenta o board "Stack" do /Diagnostico. /health nao usa envelope
        // {data}, entao GetJsonAsync; injeta Bearer mas /health e anonimo no upstream.
        app.MapGet("/api-proxy/diag/health", async (
            EasyStock.Admin.Services.AdminApiClient api,
            EasyStock.Admin.Services.AdminSessionService session,
            ILogger<Program> log) =>
        {
            if (string.IsNullOrEmpty(session.GetToken())) return Results.Unauthorized();
            try
            {
                var data = await api.GetJsonAsync<JsonElement>("health");
                return Results.Ok(data);
            }
            catch (EasyStock.Admin.Services.SessionExpiredException) { return Results.Unauthorized(); }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Proxy diag/health falhou");
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

    }
}
