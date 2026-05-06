using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tenants;

public class IndexModel(AdminApiClient api, AdminSessionService session, IConfiguration config, ILogger<IndexModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public JsonElement Data { get; private set; }
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Ativa", "Suspensa", "Cancelada" };

    public IEnumerable<JsonElement> Tenants => Data.ValueKind == JsonValueKind.Array
        ? Data.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public async Task OnGetAsync()
    {
        if (Page < 1) Page = 1;
        if (Page > 10000) Page = 10000;
        try
        {
            var qs = $"api/admin/tenants?page={Page}&pageSize=20";
            if (!string.IsNullOrWhiteSpace(Search)) qs += $"&search={Uri.EscapeDataString(Search)}";
            if (!string.IsNullOrWhiteSpace(Status) && StatusValidos.Contains(Status))
                qs += $"&status={Uri.EscapeDataString(Status)}";

            var raw = await api.GetRawAsync(qs);
            // Resposta de erro pode não ter `data` — TryGetProperty evita KeyNotFound feio.
            Data = raw.TryGetProperty("data", out var d) ? d : default;
            if (raw.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var t) && t.TryGetInt32(out var tv) ? tv : 0;
                TotalPages = meta.TryGetProperty("pages", out var p) && p.TryGetInt32(out var pv) ? pv : 1;
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar tenants");
            Erro = "Não foi possível carregar a lista de tenants. Tente recarregar a página.";
        }
    }

    public async Task<IActionResult> OnPostSuspenderAsync(Guid id, string motivo)
    {
        if (id == Guid.Empty)
        {
            SetErro("Tenant inválido.");
            return RedirectToPage(new { Page, Search, Status });
        }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 3)
        {
            SetErro("Informe um motivo com pelo menos 3 caracteres.");
            return RedirectToPage(new { Page, Search, Status });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tenants/{id}/status",
                new { status = "Suspensa", motivo = motivoT });
            SetSucesso("Tenant suspenso com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao suspender tenant {TenantId}", id);
            SetErro($"Falha ao suspender tenant: {ex.Message}");
        }
        return RedirectToPage(new { Page, Search, Status });
    }

    public async Task<IActionResult> OnPostReativarAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            SetErro("Tenant inválido.");
            return RedirectToPage(new { Page, Search, Status });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tenants/{id}/status",
                new { status = "Ativa", motivo = "Reativado pelo admin" });
            SetSucesso("Tenant reativado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao reativar tenant {TenantId}", id);
            SetErro($"Falha ao reativar tenant: {ex.Message}");
        }
        return RedirectToPage(new { Page, Search, Status });
    }

    public async Task<IActionResult> OnPostImpersonarAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            SetErro("Tenant inválido.");
            return RedirectToPage(new { Page, Search, Status });
        }
        try
        {
            var result = await api.PostAsync<JsonElement>($"api/admin/tenants/{id}/impersonate", new { });
            var token = result.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(token))
            {
                SetErro("API não retornou token de impersonation.");
                return RedirectToPage(new { Page, Search, Status });
            }
            var webUrl = ResolveWebUrl(config);
            if (webUrl is null)
            {
                log.LogError("EasyStockWebUrl ausente ou inválido — impossível fazer handoff de impersonation.");
                SetErro("Configuração de URL do EasyStock.Web inválida. Contate o administrador do sistema.");
                return RedirectToPage(new { Page, Search, Status });
            }
            // POST handoff em vez de GET com token na URL — token não vaza em
            // logs/history/referrer. Renderiza HTML com auto-submit form.
            return Content(BuildHandoffHtml(webUrl, token), "text/html; charset=utf-8");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao impersonar tenant {TenantId}", id);
            SetErro($"Falha ao impersonar: {ex.Message}");
            return RedirectToPage(new { Page, Search, Status });
        }
    }

    /// <summary>
    /// Valida e normaliza EasyStockWebUrl. Retorna null se config inválida —
    /// melhor não fazer handoff do que apontar pra URL maliciosa/typo.
    /// </summary>
    internal static string? ResolveWebUrl(IConfiguration config)
    {
        var raw = config["EasyStockWebUrl"]?.Trim();
        if (string.IsNullOrEmpty(raw)) return null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;
        return uri.GetLeftPart(UriPartial.Authority);
    }

    internal static string BuildHandoffHtml(string webUrl, string token)
    {
        var safeToken = System.Net.WebUtility.HtmlEncode(token);
        var safeUrl = System.Net.WebUtility.HtmlEncode(webUrl);
        return $$"""
        <!doctype html><html><head><meta charset="utf-8"><title>Conectando…</title>
        <meta http-equiv="Cache-Control" content="no-store, no-cache, must-revalidate" />
        <meta name="robots" content="noindex, nofollow" /></head>
        <body style="font-family:system-ui;background:#0f172a;color:#cbd5e1;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
        <form id="f" method="POST" action="{{safeUrl}}/auth/impersonate" autocomplete="off">
            <input type="hidden" name="token" value="{{safeToken}}" />
            <p>Iniciando sessão de suporte…</p>
            <noscript><button type="submit">Continuar</button></noscript>
        </form>
        <script>document.getElementById('f').submit();</script>
        </body></html>
        """;
    }
}
