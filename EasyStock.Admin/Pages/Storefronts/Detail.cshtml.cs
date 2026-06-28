namespace EasyStock.Admin.Pages.Storefronts;

public class DetailModel(AdminApiClient api, AdminSessionService session, ILogger<DetailModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public JsonElement Storefront { get; private set; }
    public bool Encontrado { get; private set; }

    /// <summary>Itens de cardápio publicados (visíveis) — sinal de prontidão para ativar.</summary>
    public int CardapioPublicados { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id == Guid.Empty) return RedirectToPage("/Storefronts/Index");

        try
        {
            var raw = await api.GetRawAsync($"api/admin/storefronts/{Id}");
            if (raw.TryGetProperty("data", out var data))
            {
                Storefront = data;
                Encontrado = true;
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao obter storefront {Id}", Id);
            SetErroSeguro(ex, "Carregamento");
        }

        // Prontidão: conta itens publicados (visíveis) via o endpoint de cardápio que já existe.
        // BFF-side (sem tocar o backend); falha aqui não derruba o detalhe.
        if (Encontrado)
        {
            try
            {
                var card = await api.GetRawAsync($"api/admin/storefronts/{Id}/cardapio");
                if (card.TryGetProperty("data", out var cd)
                    && cd.TryGetProperty("itens", out var itens)
                    && itens.ValueKind == JsonValueKind.Array)
                {
                    CardapioPublicados = itens.EnumerateArray()
                        .Count(i => i.TryGetProperty("visivel", out var v) && v.GetBoolean());
                }
            }
            catch (SessionExpiredException) { throw; }
            catch (Exception ex) { log.LogWarning(ex, "Falha ao contar cardápio publicado {Id}", Id); }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAtivarAsync(string? motivo)
    {
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/storefronts/{Id}/ativar", new { motivo });
            SetSucesso("Storefront ativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao ativar storefront {Id}", Id);
            SetErroSeguro(ex, "Ativação");
        }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostDesativarAsync(string? motivo)
    {
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/storefronts/{Id}/desativar", new { motivo });
            SetSucesso("Storefront desativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao desativar storefront {Id}", Id);
            SetErroSeguro(ex, "Desativação");
        }
        return RedirectToPage(new { id = Id });
    }
}
