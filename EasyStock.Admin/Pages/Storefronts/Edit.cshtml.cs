using System.ComponentModel.DataAnnotations;

namespace EasyStock.Admin.Pages.Storefronts;

public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> log)
    : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty] public EditInput Input { get; set; } = new();

    public string SlugAtual { get; private set; } = "";
    public string TituloAtual { get; private set; } = "";
    public bool Ativo { get; private set; }

    public sealed class EditInput
    {
        [StringLength(120)] public string? SubtituloPublico { get; set; }
        [StringLength(500)] public string? LogoUrl { get; set; }
        [RegularExpression(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$",
            ErrorMessage = "Cor deve estar em hex (#RGB ou #RRGGBB).")]
        public string? CorPrimaria { get; set; }
        [RegularExpression(@"^\+?\d{10,15}$",
            ErrorMessage = "WhatsApp em formato E.164 (ex: +5511997573992).")]
        public string? WhatsappPedidos { get; set; }
        [StringLength(500)] public string? MensagemForaArea { get; set; }
        [Range(0, 100000)] public decimal? PedidoMinimoEntrega { get; set; }
        [Range(0, 1000000)] public decimal? FreteGratisAcima { get; set; }
        [StringLength(100)] public string? DominioCustom { get; set; }
        public string? ModeloFiscal { get; set; }
        public bool? HabilitarNfeAutomatica { get; set; }
        public Guid? LojaPadraoId { get; set; }
        [StringLength(500)] public string? Motivo { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id == Guid.Empty) return RedirectToPage("/Storefronts/Index");
        try
        {
            var raw = await api.GetRawAsync($"api/admin/storefronts/{Id}");
            if (raw.TryGetProperty("data", out var data))
            {
                SlugAtual = data.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
                TituloAtual = data.TryGetProperty("tituloPublico", out var t) ? t.GetString() ?? "" : "";
                Ativo = data.TryGetProperty("ativo", out var a) && a.GetBoolean();
                Input = new EditInput
                {
                    SubtituloPublico = data.TryGetProperty("subtituloPublico", out var sub) ? sub.GetString() : null,
                    LogoUrl = data.TryGetProperty("logoUrl", out var lo) ? lo.GetString() : null,
                    CorPrimaria = data.TryGetProperty("corPrimaria", out var cp) ? cp.GetString() : null,
                    WhatsappPedidos = data.TryGetProperty("whatsappPedidos", out var wa) ? wa.GetString() : null,
                    MensagemForaArea = data.TryGetProperty("mensagemForaArea", out var mfa) ? mfa.GetString() : null,
                    PedidoMinimoEntrega = data.TryGetProperty("pedidoMinimoEntrega", out var pm) && pm.TryGetDecimal(out var pmv) ? pmv : null,
                    FreteGratisAcima = data.TryGetProperty("freteGratisAcima", out var fg) && fg.ValueKind == JsonValueKind.Number && fg.TryGetDecimal(out var fgv) ? fgv : null,
                    DominioCustom = data.TryGetProperty("dominioCustom", out var dc) ? dc.GetString() : null,
                    ModeloFiscal = data.TryGetProperty("modeloFiscal", out var mf) ? mf.GetString() : null,
                    HabilitarNfeAutomatica = data.TryGetProperty("nfeAutomaticaHabilitada", out var nf) ? nf.GetBoolean() : null,
                    LojaPadraoId = data.TryGetProperty("lojaPadraoId", out var lp) && lp.ValueKind != JsonValueKind.Null
                        && Guid.TryParse(lp.GetString(), out var lpv) ? lpv : null,
                };
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar storefront {Id}", Id);
            SetErroSeguro(ex, "Carregamento");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            await api.PutAsync($"api/admin/storefronts/{Id}", new
            {
                subtituloPublico = Input.SubtituloPublico,
                logoUrl = Input.LogoUrl,
                corPrimaria = Input.CorPrimaria,
                whatsappPedidos = Input.WhatsappPedidos,
                mensagemForaArea = Input.MensagemForaArea,
                pedidoMinimoEntrega = Input.PedidoMinimoEntrega,
                freteGratisAcima = Input.FreteGratisAcima,
                dominioCustom = Input.DominioCustom,
                modeloFiscal = Input.ModeloFiscal,
                habilitarNfeAutomatica = Input.HabilitarNfeAutomatica,
                lojaPadraoId = Input.LojaPadraoId,
                motivo = Input.Motivo
            });
            SetSucesso("Storefront atualizado.");
            return RedirectToPage("/Storefronts/Detail", new { id = Id });
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao atualizar storefront {Id}", Id);
            SetErroSeguro(ex, "Atualização");
            return Page();
        }
    }
}
