using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Envios;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public Guid? EmpresaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Canal { get; set; }
    [BindProperty(SupportsGet = true)] public string? De { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ate { get; set; }

    public JsonElement Data { get; private set; }
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var qs = $"?page={Page}&pageSize=20";
            if (EmpresaId.HasValue) qs += $"&empresaId={EmpresaId}";
            if (!string.IsNullOrWhiteSpace(Status)) qs += $"&status={Status}";
            if (!string.IsNullOrWhiteSpace(Canal)) qs += $"&canal={Canal}";
            if (!string.IsNullOrWhiteSpace(De)) qs += $"&de={De}";
            if (!string.IsNullOrWhiteSpace(Ate)) qs += $"&ate={Ate}";

            var result = await api.GetRawAsync($"api/admin/notificacoes/envios{qs}");
            Data = result.GetProperty("data");
            if (result.TryGetProperty("meta", out var meta))
            {
                Total = meta.GetProperty("total").GetInt32();
                TotalPages = meta.GetProperty("pages").GetInt32();
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar envios");
            Erro = "Erro ao carregar log de envios.";
        }
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        try
        {
            var qs = "?page=1&pageSize=1000";
            if (EmpresaId.HasValue) qs += $"&empresaId={EmpresaId}";
            if (!string.IsNullOrWhiteSpace(Status)) qs += $"&status={Status}";
            if (!string.IsNullOrWhiteSpace(Canal)) qs += $"&canal={Canal}";

            var (bytes, ct) = await api.GetBytesAsync($"api/admin/notificacoes/envios/export{qs}");
            return File(bytes, ct, $"envios-{DateTime.UtcNow:yyyyMMdd}.csv");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao exportar envios");
            SetErro("Erro ao exportar.");
            return RedirectToPage();
        }
    }
}
