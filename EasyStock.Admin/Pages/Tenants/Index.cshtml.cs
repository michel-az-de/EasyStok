using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tenants;

public class IndexModel(AdminApiClient api, AdminSessionService session, IConfiguration config) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }

    public JsonElement Data { get; private set; }
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    public IEnumerable<JsonElement> Tenants => Data.ValueKind != JsonValueKind.Undefined
        ? Data.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public async Task OnGetAsync()
    {
        try
        {
            var qs = $"api/admin/tenants?page={Page}&pageSize=20";
            if (!string.IsNullOrWhiteSpace(Search)) qs += $"&search={Uri.EscapeDataString(Search)}";
            if (!string.IsNullOrWhiteSpace(Status)) qs += $"&status={Status}";

            var raw = await api.GetRawAsync(qs);
            Data = raw.GetProperty("data");
            if (raw.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                TotalPages = meta.TryGetProperty("pages", out var p) ? p.GetInt32() : 1;
            }
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostSuspenderAsync(Guid id, string motivo)
    {
        await api.PatchAsync<JsonElement>($"api/admin/tenants/{id}/status",
            new { status = "Suspensa", motivo });
        SetSucesso("Tenant suspenso com sucesso.");
        return RedirectToPage(new { Page, Search, Status });
    }

    public async Task<IActionResult> OnPostReativarAsync(Guid id)
    {
        await api.PatchAsync<JsonElement>($"api/admin/tenants/{id}/status",
            new { status = "Ativa", motivo = "Reativado pelo admin" });
        SetSucesso("Tenant reativado com sucesso.");
        return RedirectToPage(new { Page, Search, Status });
    }

    public async Task<IActionResult> OnPostImpersonarAsync(Guid id)
    {
        try
        {
            var result = await api.PostAsync<JsonElement>($"api/admin/tenants/{id}/impersonate", new { });
            var token = result.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(token))
            {
                SetErro("API não retornou token de impersonation.");
                return RedirectToPage(new { Page, Search, Status });
            }
            var webUrl = config["EasyStockWebUrl"]?.TrimEnd('/') ?? "https://localhost:7001";
            // POST handoff em vez de GET com token na URL — token não vaza em
            // logs/history/referrer. Renderiza HTML com auto-submit form.
            return Content(BuildHandoffHtml(webUrl, token), "text/html; charset=utf-8");
        }
        catch (Exception ex)
        {
            SetErro($"Falha ao impersonar: {ex.Message}");
            return RedirectToPage(new { Page, Search, Status });
        }
    }

    internal static string BuildHandoffHtml(string webUrl, string token)
    {
        var safeToken = System.Net.WebUtility.HtmlEncode(token);
        return $$"""
        <!doctype html><html><head><meta charset="utf-8"><title>Conectando…</title></head>
        <body style="font-family:system-ui;background:#0f172a;color:#cbd5e1;display:flex;align-items:center;justify-content:center;height:100vh;margin:0">
        <form id="f" method="POST" action="{{webUrl}}/auth/impersonate">
            <input type="hidden" name="token" value="{{safeToken}}" />
            <p>Iniciando sessão de suporte…</p>
            <noscript><button type="submit">Continuar</button></noscript>
        </form>
        <script>document.getElementById('f').submit();</script>
        </body></html>
        """;
    }
}
