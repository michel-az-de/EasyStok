using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Diagnostico;

/// <summary>
/// Página unificada de diagnóstico — substitui o /Status antigo. Tabs:
/// Resumo (health-check, equivalente ao /Status anterior), Erros (lista
/// paginada server-side com filtros) e Operações (seed + manutenção de logs).
/// Live/Insights são P1/P2.
/// </summary>
public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    /// <summary>Carrega no GET pra hidratar a tab Resumo sem flash. Demais tabs lazy-load.</summary>
    public JsonElement StatusData { get; private set; }
    public string? Erro { get; private set; }

    /// <summary>Tab inicial — controlada via QS pra deep-link.</summary>
    [BindProperty(SupportsGet = true)]
    public string Tab { get; set; } = "resumo";

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

    // ─────────────────── Seed (handlers — migrados do /Status) ───────────────────

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

    public async Task<IActionResult> OnPostSeedAdminScenariosAsync()
    {
        try
        {
            var data = await api.PostAsync<JsonElement>("api/admin/seed/admin-test-scenarios", new { });
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao executar seed admin-test-scenarios");
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }
}
