namespace EasyStock.Admin.Pages.Banners;

/// <summary>Listagem e ações rápidas dos banners de plataforma (#869). SuperAdmin apenas.</summary>
public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public IReadOnlyList<JsonElement> Itens { get; private set; } = Array.Empty<JsonElement>();
    public int Total { get; private set; }
    public int Page { get; private set; } = 1;
    public bool? Ativo { get; private set; }

    public async Task OnGetAsync([FromQuery] int page = 1, [FromQuery] string? ativo = null)
    {
        Page = Math.Max(1, page);
        Ativo = ativo switch { "1" => true, "0" => false, _ => null };

        try
        {
            var q = $"api/admin/banners?page={Page}&pageSize=20";
            if (Ativo is { } a) q += $"&ativo={(a ? "true" : "false")}";

            var raw = await api.GetRawAsync(q);
            if (raw.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                Itens = data.EnumerateArray().ToList();
            if (raw.TryGetProperty("meta", out var meta) && meta.TryGetProperty("total", out var t))
                Total = t.GetInt32();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar banners");
            SetErroSeguro(ex, "Listagem de banners");
        }
    }

    public Task<IActionResult> OnPostAtivarAsync(Guid id) => AcaoAsync($"api/admin/banners/{id}/ativar", "Banner ativado.");
    public Task<IActionResult> OnPostDesativarAsync(Guid id) => AcaoAsync($"api/admin/banners/{id}/desativar", "Banner desativado.");

    public async Task<IActionResult> OnPostExcluirAsync(Guid id)
    {
        try
        {
            await api.DeleteAsync($"api/admin/banners/{id}");
            SetSucesso("Banner excluído.");
        }
        catch (ApiException ex)
        {
            // 409 (banner com confirmações) chega como ApiException com a mensagem amigável.
            SetErro(ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao excluir banner {Id}", id);
            SetErroSeguro(ex, "Exclusão de banner");
        }
        return RedirectToPage("/Banners/Index");
    }

    private async Task<IActionResult> AcaoAsync(string path, string sucesso)
    {
        try
        {
            await api.PostAsync(path, new { });
            SetSucesso(sucesso);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha na ação {Path}", path);
            SetErroSeguro(ex, "Ação em banner");
        }
        return RedirectToPage("/Banners/Index");
    }
}
