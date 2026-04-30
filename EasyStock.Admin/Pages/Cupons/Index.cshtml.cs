using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Cupons;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Cupons { get; private set; } = Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> Planos { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }
    public string? Mensagem { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            Cupons = await api.GetAsync<IEnumerable<JsonElement>>("api/admin/cupons");
            var planosRaw = await api.GetAsync<JsonElement>("api/admin/planos");
            Planos = planosRaw.ValueKind == JsonValueKind.Array
                ? planosRaw.EnumerateArray().ToList()
                : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostCriarAsync(
        string codigo, string tipoDesconto, decimal valor,
        int? limiteUsos, string? validoAte, string? planoId)
    {
        DateTime? validoAteDt = string.IsNullOrWhiteSpace(validoAte) ? null : DateTime.Parse(validoAte);
        Guid? planoIdGuid = string.IsNullOrWhiteSpace(planoId) ? null : Guid.Parse(planoId);

        try
        {
            await api.PostAsync<JsonElement>("api/admin/cupons",
                new { codigo, tipoDesconto, valor, limiteUsos, validoAte = validoAteDt, planoId = planoIdGuid });
        }
        catch (Exception ex) { Erro = ex.Message; }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostEditarAsync(
        Guid id, string codigo, string tipoDesconto, decimal valor,
        int? limiteUsos, string? validoAte, string? planoId)
    {
        DateTime? validoAteDt = string.IsNullOrWhiteSpace(validoAte) ? null : DateTime.Parse(validoAte);
        Guid? planoIdGuid = string.IsNullOrWhiteSpace(planoId) ? null : Guid.Parse(planoId);

        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/cupons/{id}",
                new { codigo, tipoDesconto, valor, limiteUsos, validoAte = validoAteDt, planoId = planoIdGuid });
        }
        catch (Exception ex) { Erro = ex.Message; }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        try { await api.PatchAsync<JsonElement>($"api/admin/cupons/{id}/toggle", new { }); }
        catch (Exception ex) { Erro = ex.Message; }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeletarAsync(Guid id)
    {
        try { await api.DeleteAsync($"api/admin/cupons/{id}"); }
        catch (Exception ex) { Erro = ex.Message; }
        return RedirectToPage();
    }
}
