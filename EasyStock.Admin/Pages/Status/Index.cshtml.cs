using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Status;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public JsonElement StatusData { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            StatusData = await api.GetAsync<JsonElement>("api/admin/status");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar status do sistema");
            Erro = "Não foi possível carregar o status. Verifique se a API está acessível.";
        }
    }

    // ─────────────────── Seed (handlers) ───────────────────

    public async Task<IActionResult> OnGetSeedStatusAsync()
    {
        try
        {
            var data = await api.GetRawAsync("api/admin/seed/status");
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao consultar status do seed");
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostSeedDemoAsync(string? volume = null)
    {
        try
        {
            var qs = string.IsNullOrWhiteSpace(volume) ? "" : $"?volume={Uri.EscapeDataString(volume)}";
            var data = await api.PostAsync<JsonElement>($"api/admin/seed/demo{qs}", new { });
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao executar seed demo");
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostSeedMinimalAsync()
    {
        try
        {
            var data = await api.PostAsync<JsonElement>("api/admin/seed/minimal", new { });
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao executar seed minimal");
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }
}
