using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.AuditLogs;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Logs { get; private set; } = Enumerable.Empty<JsonElement>();
    public int Total { get; private set; }
    public int CurrentPage { get; private set; } = 1;
    public int PageSize { get; private set; } = 50;
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    [BindProperty(SupportsGet = true)] public string? TenantId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Acao { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNum { get; set; } = 1;

    public async Task OnGetAsync()
    {
        CurrentPage = Math.Max(1, PageNum);
        try
        {
            var qs = BuildQueryString(CurrentPage);
            var root = await api.GetRawAsync($"api/admin/audit-logs?{qs}");

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                Logs = data.EnumerateArray().ToList();

            if (root.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var tp) ? tp.GetInt32() : 0;
                PageSize = meta.TryGetProperty("limit", out var lp) ? lp.GetInt32() : 50;
                TotalPages = PageSize > 0 ? (int)Math.Ceiling((double)Total / PageSize) : 1;
            }
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    private string BuildQueryString(int page)
    {
        var parts = new List<string> { $"page={page}", "pageSize=50" };
        if (!string.IsNullOrWhiteSpace(TenantId)) parts.Add($"tenantId={Uri.EscapeDataString(TenantId)}");
        if (!string.IsNullOrWhiteSpace(Acao)) parts.Add($"acao={Uri.EscapeDataString(Acao)}");
        if (!string.IsNullOrWhiteSpace(From)) parts.Add($"from={Uri.EscapeDataString(From)}");
        if (!string.IsNullOrWhiteSpace(To)) parts.Add($"to={Uri.EscapeDataString(To)}");
        return string.Join("&", parts);
    }

    public string BuildCsvUrl()
    {
        var parts = new List<string> { "export=csv" };
        if (!string.IsNullOrWhiteSpace(TenantId)) parts.Add($"tenantId={Uri.EscapeDataString(TenantId)}");
        if (!string.IsNullOrWhiteSpace(Acao)) parts.Add($"acao={Uri.EscapeDataString(Acao)}");
        if (!string.IsNullOrWhiteSpace(From)) parts.Add($"from={Uri.EscapeDataString(From)}");
        if (!string.IsNullOrWhiteSpace(To)) parts.Add($"to={Uri.EscapeDataString(To)}");
        return $"/api-proxy/audit-logs-csv?{string.Join("&", parts)}";
    }
}
