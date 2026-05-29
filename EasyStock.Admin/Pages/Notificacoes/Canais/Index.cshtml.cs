namespace EasyStock.Admin.Pages.Notificacoes.Canais;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> logger)
    : AdminPageBase(session)
{
    public JsonElement? Configs { get; private set; }
    public JsonElement? Bloqueios { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var result = await api.GetRawAsync("api/admin/notificacoes/canais");
            var data = result.GetProperty("data");
            Configs = data.GetProperty("configs");
            Bloqueios = data.GetProperty("bloqueios");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Erro ao listar canais");
            Erro = "Erro ao carregar configurações de canal.";
        }
    }

    public async Task<IActionResult> OnPostKillSwitchAsync(string motivo, string? canal)
    {
        try
        {
            await api.PostRawAsync("api/admin/notificacoes/canais/kill-switch", new
            {
                motivo,
                canal = string.IsNullOrWhiteSpace(canal) ? null : canal
            });
            SetSucesso("Kill-switch ativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex) { SetErroSeguro(ex, "Operacao em canais de notificacao"); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRemoverKillSwitchAsync(Guid id, string? motivo = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(motivo))
                logger.LogInformation("Removendo kill-switch {Id}. Motivo: {Motivo}", id, motivo);
            await api.DeleteAsync($"api/admin/notificacoes/canais/kill-switch/{id}");
            SetSucesso("Kill-switch removido.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex) { SetErroSeguro(ex, "Operacao em canais de notificacao"); }
        return RedirectToPage();
    }
}
