namespace EasyStock.Admin.Pages.Notificacoes.Rotinas;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> logger)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public bool? Ativa { get; set; }

    public JsonElement Data { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var qs = $"?page={Page}&pageSize=20";
            if (Ativa.HasValue) qs += $"&ativa={Ativa.Value}";

            var result = await api.GetRawAsync($"api/admin/notificacoes/rotinas{qs}");
            Data = result.GetProperty("data");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar rotinas");
            Erro = "Erro ao carregar rotinas.";
        }
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool ativa, string? motivo = null)
    {
        try
        {
            var endpoint = ativa
                ? $"api/admin/notificacoes/rotinas/{id}/desativar"
                : $"api/admin/notificacoes/rotinas/{id}/ativar";
            if (ativa && !string.IsNullOrWhiteSpace(motivo))
                logger.LogInformation("Desativando rotina {Id}. Motivo: {Motivo}", id, motivo);
            await api.PatchRawAsync(endpoint, new { });
            SetSucesso(ativa ? "Rotina desativada." : "Rotina ativada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex) { SetErro(ex.Message); }
        return RedirectToPage();
    }
}
