using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Admins;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Admins { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }
    public string? NovaSenha { get; private set; }
    public string? MensagemSucesso { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var raw = await api.GetRawAsync("api/admin/admins");
            Admins = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostCriarAsync(string nome, string email, string senha)
    {
        try
        {
            await api.PostRawAsync("api/admin/admins", new { nome, email, senha });
            SetSucesso("Admin criado com sucesso.");
        }
        catch (Exception ex) { SetErro(ex.Message); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/admins/{id}/toggle", new { });
            SetSucesso("Status alterado.");
        }
        catch (Exception ex) { SetErro(ex.Message); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetSenhaAsync(Guid id)
    {
        try
        {
            var raw = await api.PostRawAsync($"api/admin/admins/{id}/reset-senha", new { });
            if (raw.TryGetProperty("data", out var d) && d.TryGetProperty("novaSenha", out var s))
                TempData["NovaSenha"] = s.GetString();
            TempData["ResetAdminId"] = id.ToString();
        }
        catch (Exception ex) { SetErro(ex.Message); }
        return RedirectToPage();
    }
}
