namespace EasyStock.Admin.Pages.Configuracoes;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public Dictionary<string, string> Config { get; private set; } = new();
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var raw = await api.GetRawAsync("api/admin/configuracoes");
            if (raw.TryGetProperty("data", out var d))
            {
                foreach (var item in d.EnumerateArray())
                {
                    var chave = item.TryGetProperty("chave", out var c) ? c.GetString() : null;
                    var valor = item.TryGetProperty("valor", out var v) ? v.GetString() : "";
                    if (chave != null) Config[chave] = valor ?? "";
                }
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar configuracoes");
            Erro = ex.Message;
        }
    }

    public async Task<IActionResult> OnPostDisponibilidadeAsync(
        string manutencao_ativa, string aviso_global, string aviso_cor)
    {
        try
        {
            await api.PatchAsync<JsonElement>("api/admin/configuracoes", new
            {
                items = new[]
                {
                    new { chave = "manutencao_ativa", valor = manutencao_ativa },
                    new { chave = "aviso_global",     valor = aviso_global },
                    new { chave = "aviso_cor",        valor = aviso_cor },
                }
            });
            SetSucesso("Configurações de disponibilidade salvas.");
        }
        catch (Exception ex) { SetErroSeguro(ex, "Salvar configuracoes"); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostOnboardingAsync(string dias_trial_padrao, string email_suporte)
    {
        try
        {
            await api.PatchAsync<JsonElement>("api/admin/configuracoes", new
            {
                items = new[]
                {
                    new { chave = "dias_trial_padrao", valor = dias_trial_padrao },
                    new { chave = "email_suporte",     valor = email_suporte },
                }
            });
            SetSucesso("Configurações de onboarding salvas.");
        }
        catch (Exception ex) { SetErroSeguro(ex, "Salvar configuracoes"); }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostPwaAsync(string versao_minima_pwa)
    {
        try
        {
            await api.PatchAsync<JsonElement>("api/admin/configuracoes", new
            {
                items = new[] { new { chave = "versao_minima_pwa", valor = versao_minima_pwa } }
            });
            SetSucesso("Configurações PWA salvas.");
        }
        catch (Exception ex) { SetErroSeguro(ex, "Salvar configuracoes"); }
        return RedirectToPage();
    }

    public string Get(string chave, string @default = "") =>
        Config.TryGetValue(chave, out var v) ? v : @default;
}
