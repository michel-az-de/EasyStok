using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tickets;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Prioridade { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }

    public IEnumerable<JsonElement> Tickets { get; private set; } = Enumerable.Empty<JsonElement>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public IEnumerable<JsonElement> Empresas { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var qs = $"api/admin/tickets?page={Page}&pageSize=25";
            if (!string.IsNullOrWhiteSpace(Status)) qs += $"&status={Status}";
            if (!string.IsNullOrWhiteSpace(Prioridade)) qs += $"&prioridade={Prioridade}";
            if (!string.IsNullOrWhiteSpace(Search)) qs += $"&search={Uri.EscapeDataString(Search)}";

            var raw = await api.GetRawAsync(qs);
            Tickets = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
            if (raw.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                TotalPages = meta.TryGetProperty("pages", out var p) ? p.GetInt32() : 1;
            }
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostAsync(Guid empresaId, string titulo, string descricao, string categoria, string prioridade)
    {
        await api.PostAsync<JsonElement>("api/admin/tickets",
            new { empresaId, titulo, descricao, categoria, prioridade });
        SetSucesso("Ticket criado com sucesso.");
        return RedirectToPage(new { Page, Status, Prioridade, Search });
    }
}
