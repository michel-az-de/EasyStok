using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Storefronts;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ativo { get; set; }

    public JsonElement Data { get; private set; }
    public int Total { get; private set; }
    public int TotalPages { get; private set; }

    private const int PageSize = 20;
    private const int MaxPage = 10000;

    public IEnumerable<JsonElement> Storefronts => Data.ValueKind == JsonValueKind.Array
        ? Data.EnumerateArray()
        : Enumerable.Empty<JsonElement>();

    public async Task OnGetAsync()
    {
        if (Page < 1) Page = 1;
        if (Page > MaxPage) Page = MaxPage;

        try
        {
            var qs = $"api/admin/storefronts?page={Page}&pageSize={PageSize}";
            if (!string.IsNullOrWhiteSpace(Search))
                qs += $"&search={Uri.EscapeDataString(Search.Trim())}";
            if (!string.IsNullOrWhiteSpace(Ativo) && bool.TryParse(Ativo, out var a))
                qs += $"&ativo={a.ToString().ToLowerInvariant()}";

            var raw = await api.GetRawAsync(qs);
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
            log.LogError(ex, "Falha ao listar storefronts");
            SetErroSeguro(ex, "Listagem");
        }
    }

    public async Task<IActionResult> OnPostAtivarAsync(Guid id, string? motivo)
    {
        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{id}/ativar",
                new { motivo });
            SetSucesso("Storefront ativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao ativar storefront {Id}", id);
            SetErroSeguro(ex, "Ativação");
        }
        return RedirectToPage(new { Page, Search, Ativo });
    }

    public async Task<IActionResult> OnPostDesativarAsync(Guid id, string? motivo)
    {
        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{id}/desativar",
                new { motivo });
            SetSucesso("Storefront desativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao desativar storefront {Id}", id);
            SetErroSeguro(ex, "Desativação");
        }
        return RedirectToPage(new { Page, Search, Ativo });
    }
}
