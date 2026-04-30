using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tickets;

public class DetailModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public JsonElement TicketData { get; private set; }
    public string? Erro { get; private set; }

    public string Titulo => Str("titulo");
    public string Status => Str("status");
    public string Prioridade => Str("prioridade");
    public string Categoria => Str("categoria");
    public string EmpresaNome => Str("empresaNome");
    public string EmpresaId => Str("empresaId");
    public string AtendenteNome => Str("atendenteNome");

    public IEnumerable<JsonElement> Mensagens =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty("mensagens", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private string Str(string k) =>
        TicketData.ValueKind != JsonValueKind.Undefined && TicketData.TryGetProperty(k, out var v)
        && v.ValueKind != JsonValueKind.Null ? v.GetString() ?? "" : "";

    public async Task OnGetAsync()
    {
        try
        {
            TicketData = await api.GetAsync<JsonElement>($"api/admin/tickets/{Id}");
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostResponderAsync(string conteudo)
    {
        await api.PostAsync<JsonElement>($"api/admin/tickets/{Id}/mensagens", new { conteudo });
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostResolverAsync()
    {
        await api.PatchAsync<JsonElement>($"api/admin/tickets/{Id}", new { status = "Resolvido" });
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostAlterarStatusAsync(string status)
    {
        await api.PatchAsync<JsonElement>($"api/admin/tickets/{Id}", new { status });
        return RedirectToPage(new { Id });
    }
}
