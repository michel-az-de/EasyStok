using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Storefronts.Cardapio;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid StorefrontId { get; set; }

    public string StorefrontSlug { get; private set; } = "";
    public string StorefrontTitulo { get; private set; } = "";
    public IEnumerable<JsonElement> Itens { get; private set; } = Enumerable.Empty<JsonElement>();

    public async Task<IActionResult> OnGetAsync()
    {
        if (StorefrontId == Guid.Empty) return RedirectToPage("/Storefronts/Index");

        try
        {
            var raw = await api.GetRawAsync($"api/admin/storefronts/{StorefrontId}/cardapio");
            if (raw.TryGetProperty("data", out var data))
            {
                StorefrontSlug = data.TryGetProperty("storefrontSlug", out var s) ? s.GetString() ?? "" : "";
                StorefrontTitulo = data.TryGetProperty("storefrontTitulo", out var t) ? t.GetString() ?? "" : "";
                if (data.TryGetProperty("itens", out var it) && it.ValueKind == JsonValueKind.Array)
                {
                    Itens = it.EnumerateArray().ToList();
                }
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar cardápio {StorefrontId}", StorefrontId);
            SetErroSeguro(ex, "Carregamento");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostToggleVisivelAsync(Guid itemId)
    {
        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{StorefrontId}/cardapio/{itemId}/toggle-visivel",
                new { });
            SetSucesso("Visibilidade atualizada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar visibilidade {ItemId}", itemId);
            SetErroSeguro(ex, "Toggle visibilidade");
        }
        return RedirectToPage(new { StorefrontId });
    }

    public async Task<IActionResult> OnPostToggleDisponivelAsync(Guid itemId)
    {
        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{StorefrontId}/cardapio/{itemId}/toggle-disponivel",
                new { });
            SetSucesso("Disponibilidade atualizada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar disponibilidade {ItemId}", itemId);
            SetErroSeguro(ex, "Toggle disponibilidade");
        }
        return RedirectToPage(new { StorefrontId });
    }

    public async Task<IActionResult> OnPostReordenarAsync(Guid itemId, double novaOrdem)
    {
        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/storefronts/{StorefrontId}/cardapio/{itemId}/reordenar",
                new { novaOrdem });
            SetSucesso("Ordem atualizada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao reordenar {ItemId}", itemId);
            SetErroSeguro(ex, "Reordenar");
        }
        return RedirectToPage(new { StorefrontId });
    }
}
