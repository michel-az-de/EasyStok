namespace EasyStock.Admin.Pages.Storefronts;

[IgnoreAntiforgeryToken]
public class DetailModel(AdminApiClient api, AdminSessionService session, ILogger<DetailModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public JsonElement Storefront { get; private set; }
    public bool Encontrado { get; private set; }

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

        return Page();
    }
}
