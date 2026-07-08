using System.Net.Http.Headers;

namespace EasyStock.Admin.Pages.Banners;

/// <summary>
/// Cadastro/edição de banner de plataforma (#869). SuperAdmin apenas (AdminPageBase default).
/// Upload da imagem e chamada de API são server-side via AdminApiClient. Janela em datetime-local
/// (BRT) convertida para UTC antes de enviar; o preview ao vivo é 100% client (sem upload).
/// </summary>
public class EditModel(AdminApiClient api, AdminSessionService session, ILogger<EditModel> log) : AdminPageBase(session)
{
    private static readonly TimeZoneInfo Brt = ResolverFusoBrasil();

    [BindProperty(SupportsGet = true)] public Guid? Id { get; set; }

    [BindProperty] public string TituloInterno { get; set; } = string.Empty;
    [BindProperty] public string Forma { get; set; } = "imagem"; // "imagem" | "texto"
    [BindProperty] public string? Corpo { get; set; }
    [BindProperty] public IFormFile? Imagem { get; set; }
    [BindProperty] public string? ImagemStorageKey { get; set; }
    [BindProperty] public string? ImagemUrl { get; set; }
    [BindProperty] public bool LinkAtivo { get; set; }
    [BindProperty] public string? LinkUrl { get; set; }
    [BindProperty] public bool NovaAba { get; set; }
    [BindProperty] public bool TooltipAtivo { get; set; }
    [BindProperty] public string? TooltipTexto { get; set; }
    [BindProperty] public string TamanhoModo { get; set; } = "herdado"; // "herdado" | "manual"
    [BindProperty] public int? LarguraPx { get; set; }
    [BindProperty] public int? AlturaPx { get; set; }
    [BindProperty] public bool VisualizacaoUnica { get; set; }
    [BindProperty] public bool ExigeConfirmacao { get; set; }
    [BindProperty] public bool NotificarAoPublicar { get; set; }
    [BindProperty] public bool Ativo { get; set; }
    [BindProperty] public DateTime? InicioEm { get; set; }
    [BindProperty] public DateTime? FimEm { get; set; }
    [BindProperty] public int Prioridade { get; set; }

    public async Task OnGetAsync()
    {
        if (Id is not { } id || id == Guid.Empty) return;

        try
        {
            var raw = await api.GetRawAsync($"api/admin/banners/{id}");
            if (!raw.TryGetProperty("data", out var b)) return;

            TituloInterno = b.GetProperty("tituloInterno").GetString() ?? "";
            Forma = string.Equals(b.GetProperty("tipo").GetString(), "Mensagem", StringComparison.OrdinalIgnoreCase) ? "texto" : "imagem";
            Corpo = Str(b, "corpo");
            ImagemUrl = Str(b, "imagemUrl");
            TamanhoModo = string.Equals(Str(b, "tamanhoModo"), "manual", StringComparison.OrdinalIgnoreCase) ? "manual" : "herdado";
            LarguraPx = IntOpt(b, "larguraPx");
            AlturaPx = IntOpt(b, "alturaPx");
            LinkAtivo = Bool(b, "linkAtivo");
            LinkUrl = Str(b, "linkUrl");
            NovaAba = Bool(b, "novaAba");
            TooltipAtivo = Bool(b, "tooltipAtivo");
            TooltipTexto = Str(b, "tooltipTexto");
            ExigeConfirmacao = Bool(b, "exigeConfirmacao");
            VisualizacaoUnica = Bool(b, "visualizacaoUnica");
            NotificarAoPublicar = Bool(b, "notificarAoPublicar");
            Ativo = Bool(b, "ativo");
            Prioridade = b.TryGetProperty("prioridade", out var p) ? p.GetInt32() : 0;
            InicioEm = UtcParaBrt(DataOpt(b, "inicioEm"));
            FimEm = UtcParaBrt(DataOpt(b, "fimEm"));
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar banner {Id}", id);
            SetErro("Falha ao carregar o banner.");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ehTexto = string.Equals(Forma, "texto", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(TituloInterno))
            return ComErro("Informe um título interno.");
        if (ehTexto && string.IsNullOrWhiteSpace(Corpo))
            return ComErro("Banner só-texto exige a mensagem.");

        try
        {
            // Upload da imagem (se enviada) → guarda StorageKey + Url para o payload.
            if (!ehTexto && Imagem is { Length: > 0 })
            {
                using var form = new MultipartFormDataContent();
                await using var ms = new MemoryStream();
                await Imagem.CopyToAsync(ms);
                var fileContent = new ByteArrayContent(ms.ToArray());
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                    string.IsNullOrWhiteSpace(Imagem.ContentType) ? "application/octet-stream" : Imagem.ContentType);
                form.Add(fileContent, "file", Imagem.FileName);

                var up = await api.PostMultipartRawAsync("api/admin/banners/imagem", form);
                if (up.TryGetProperty("data", out var d))
                {
                    ImagemStorageKey = Str(d, "storageKey");
                    ImagemUrl = Str(d, "url");
                }
            }

            if (!ehTexto && string.IsNullOrWhiteSpace(ImagemStorageKey))
                return ComErro("Envie uma imagem (ou escolha a forma só-texto).");

            var payload = new
            {
                tituloInterno = TituloInterno,
                tipo = ehTexto ? "mensagem" : "imagem",
                corpo = ehTexto ? Corpo : null,
                imagemStorageKey = ehTexto ? null : ImagemStorageKey,
                imagemUrl = ehTexto ? null : ImagemUrl,
                linkAtivo = !ehTexto && LinkAtivo,
                linkUrl = LinkUrl,
                novaAba = NovaAba,
                tooltipAtivo = !ehTexto && TooltipAtivo,
                tooltipTexto = TooltipTexto,
                tamanhoModo = TamanhoModo,
                larguraPx = LarguraPx,
                alturaPx = AlturaPx,
                visualizacaoUnica = VisualizacaoUnica,
                exigeConfirmacao = ExigeConfirmacao,
                notificarAoPublicar = NotificarAoPublicar,
                ativo = Ativo,
                inicioEm = BrtParaUtc(InicioEm),
                fimEm = BrtParaUtc(FimEm),
                prioridade = Prioridade
            };

            if (Id is { } id && id != Guid.Empty)
            {
                await api.PutAsync($"api/admin/banners/{id}", payload);
                SetSucesso("Banner atualizado.");
            }
            else
            {
                await api.PostJsonAsync<JsonElement>("api/admin/banners", payload);
                SetSucesso("Banner criado.");
            }
            return RedirectToPage("/Banners/Index");
        }
        catch (ApiException ex)
        {
            return ComErro(ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao salvar banner");
            return ComErro("Falha ao salvar o banner.");
        }
    }

    private IActionResult ComErro(string msg)
    {
        SetErro(msg);
        return Page();
    }

    // ── Fuso: datetime-local (BRT) ↔ UTC ────────────────────────────────────
    // O input datetime-local liga como DateTime Unspecified representando a hora de parede BRT.
    private static DateTime? BrtParaUtc(DateTime? brt) => brt is { } v
        ? TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(v, DateTimeKind.Unspecified), Brt)
        : null;

    private static DateTime? UtcParaBrt(DateTime? utc) => utc is { } v
        ? DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(v.ToUniversalTime(), Brt), DateTimeKind.Unspecified)
        : null;

    private static TimeZoneInfo ResolverFusoBrasil()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT");
    }

    private static string? Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();

    private static DateTime? DataOpt(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTime(out var d) ? d : null;

    private static int? IntOpt(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;
}
