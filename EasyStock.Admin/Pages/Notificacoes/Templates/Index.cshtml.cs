using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Notificacoes.Templates;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Canal { get; set; }
    [BindProperty(SupportsGet = true)] public string? TipoEvento { get; set; }
    [BindProperty(SupportsGet = true)] public bool? Ativo { get; set; }

    public JsonElement Data { get; private set; }
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var qs = $"?page={Page}&pageSize=20";
            if (!string.IsNullOrWhiteSpace(Canal)) qs += $"&canal={Canal}";
            if (!string.IsNullOrWhiteSpace(TipoEvento)) qs += $"&tipoEvento={TipoEvento}";
            if (Ativo.HasValue) qs += $"&ativo={Ativo.Value}";

            var result = await api.GetRawAsync($"api/admin/notificacoes/templates{qs}");
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
            logger.LogError(ex, "Erro ao listar templates");
            Erro = "Erro ao carregar templates.";
        }
    }

    public async Task<IActionResult> OnPostAprovarAsync(Guid id)
    {
        try
        {
            await api.PostRawAsync($"api/admin/notificacoes/templates/{id}/aprovar", new { });
            SetSucesso("Template aprovado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex) { SetErro(ex.Message); }
        return RedirectToPage();
    }
}
