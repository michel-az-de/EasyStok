using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tenants;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
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
        return RedirectToPage(new { Page, Search, Status });
    }

    public async Task<IActionResult> OnPostReativarAsync(Guid id)
    {
        await api.PatchAsync<JsonElement>($"api/admin/tenants/{id}/status",
            new { status = "Ativa", motivo = "Reativado pelo admin" });
        return RedirectToPage(new { Page, Search, Status });
    }

    public async Task<IActionResult> OnPostImpersonarAsync(Guid id)
    {
        var result = await api.PostAsync<JsonElement>($"api/admin/tenants/{id}/impersonate", new { });
        var token = result.TryGetProperty("token", out var t) ? t.GetString() : null;
        return Redirect($"http://localhost:5000/auth/impersonate?token={Uri.EscapeDataString(token ?? "")}");
    }
}
