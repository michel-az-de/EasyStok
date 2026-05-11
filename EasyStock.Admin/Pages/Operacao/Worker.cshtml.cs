using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Operacao;

/// <summary>
/// Dashboard de saude do EasyStock.Worker. Consome
/// <c>GET /api/admin/worker-status</c> da API. SuperAdmin only (a propria API
/// rejeita 401 fora desse policy).
/// <para>
/// Hidratacao server-side no OnGet pra evitar flash, e refresh client-side a
/// cada 30s via handler <c>?handler=Data</c> (proxa pra mesma rota da API).
/// </para>
/// </summary>
public class WorkerModel(AdminApiClient api, AdminSessionService session, ILogger<WorkerModel> log)
    : AdminPageBase(session)
{
    public JsonElement Data { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            Data = Unwrap(await api.GetRawAsync("api/admin/worker-status"));
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar status do Worker");
            Erro = "Nao foi possivel carregar o status do Worker. Verifique se a API esta acessivel.";
        }
    }

    /// <summary>AJAX handler usado pelo auto-refresh de 30s.</summary>
    public async Task<IActionResult> OnGetDataAsync()
    {
        try
        {
            var data = Unwrap(await api.GetRawAsync("api/admin/worker-status"));
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha no refresh do dashboard do Worker");
            return new JsonResult(new { error = "Falha ao consultar a API." }) { StatusCode = 502 };
        }
    }

    // GetRawAsync devolve o envelope { data, meta } cru. O Razor + Alpine esperam
    // o objeto interno (saude, heartbeats, notifications, ...). Sem desempacotar,
    // a hidratacao server-side fica { data, meta } e os cards x-if="d?.saude"
    // ficam invisiveis ate o primeiro refresh (30s depois) — flash inverso.
    private static JsonElement Unwrap(JsonElement raw) =>
        raw.ValueKind == JsonValueKind.Object && raw.TryGetProperty("data", out var inner)
            ? inner
            : raw;
}
