using System.ComponentModel.DataAnnotations;

namespace EasyStock.Admin.Pages.Storefronts.Cardapio;

public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> log)
    : AdminPageBase(session)
{
    protected override bool PermiteNivelAdmin => true;

    [BindProperty(SupportsGet = true)] public Guid StorefrontId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid ItemId { get; set; }
    [BindProperty] public EditInput Input { get; set; } = new();

    public string StorefrontSlug { get; private set; } = "";
    public string ProdutoNome { get; private set; } = "";

    public sealed class EditInput
    {
        [StringLength(240)] public string? DescricaoPublica { get; set; }
        [StringLength(500)] public string? Ingredientes { get; set; }
        [StringLength(200)] public string? Alergenos { get; set; }
        [StringLength(200)] public string? SugestaoMolho { get; set; }
        [StringLength(50)] public string? TempoPreparo { get; set; }
        [StringLength(500)] public string? FotoUrl { get; set; }
        [Range(0, 100000)] public decimal? PrecoStorefront { get; set; }
        public string? Tag { get; set; }
        [StringLength(50)] public string? PesoExibicao { get; set; }
        [StringLength(500)] public string? Motivo { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (StorefrontId == Guid.Empty || ItemId == Guid.Empty)
            return RedirectToPage("/Storefronts/Index");

        try
        {
            // Reaproveita listagem para encontrar o item — endpoint dedicado existe via use case
            // mas reutilizar evita IO extra; cardápio é pequeno (raramente > 50 items).
            var raw = await api.GetRawAsync($"api/admin/storefronts/{StorefrontId}/cardapio");
            if (raw.TryGetProperty("data", out var data))
            {
                StorefrontSlug = data.TryGetProperty("storefrontSlug", out var s) ? s.GetString() ?? "" : "";
                if (data.TryGetProperty("itens", out var itens) && itens.ValueKind == JsonValueKind.Array)
                {
                    foreach (var it in itens.EnumerateArray())
                    {
                        if (it.TryGetProperty("id", out var idP)
                            && Guid.TryParse(idP.GetString(), out var idGuid)
                            && idGuid == ItemId)
                        {
                            ProdutoNome = it.TryGetProperty("produtoNome", out var pn) ? pn.GetString() ?? "" : "";
                            Input = new EditInput
                            {
                                DescricaoPublica = null, // listagem não retorna esses campos — começa vazio
                                FotoUrl = it.TryGetProperty("fotoUrl", out var fu) ? fu.GetString() : null,
                                PrecoStorefront = it.TryGetProperty("precoStorefrontOverride", out var pso) && pso.ValueKind == JsonValueKind.Number ? pso.GetDecimal() : null,
                                Tag = it.TryGetProperty("tag", out var tg) && tg.ValueKind != JsonValueKind.Null ? tg.GetString() : null,
                                PesoExibicao = it.TryGetProperty("pesoExibicao", out var pe) ? pe.GetString() : null,
                            };
                            break;
                        }
                    }
                }
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar item {ItemId}", ItemId);
            SetErroSeguro(ex, "Carregamento");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        try
        {
            await api.PutAsync(
                $"api/admin/storefronts/{StorefrontId}/cardapio/{ItemId}",
                new
                {
                    descricaoPublica = Input.DescricaoPublica,
                    ingredientes = Input.Ingredientes,
                    alergenos = Input.Alergenos,
                    sugestaoMolho = Input.SugestaoMolho,
                    tempoPreparo = Input.TempoPreparo,
                    fotoUrl = Input.FotoUrl,
                    precoStorefront = Input.PrecoStorefront,
                    tag = Input.Tag,
                    pesoExibicao = Input.PesoExibicao,
                    motivo = Input.Motivo
                });

            SetSucesso("Item atualizado.");
            return RedirectToPage("/Storefronts/Cardapio/Index", new { storefrontId = StorefrontId });
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao editar item {ItemId}", ItemId);
            SetErroSeguro(ex, "Atualização");
            return Page();
        }
    }
}
