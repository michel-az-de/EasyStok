namespace EasyStock.Admin.Pages.Banners;

/// <summary>Uma linha do log de recebimento (evento de um usuário com o aviso).</summary>
public sealed record RecebimentoEventoVM(Guid UsuarioId, string Nome, string? Empresa, string EmailMascarado, string Tipo, DateTime QuandoUtc);

/// <summary>
/// Console de recebimento de um aviso (#875): quem viu/confirmou, totais, % e quando.
/// SuperAdmin apenas. E-mail vem mascarado; a revelação do completo é um handler
/// server-side (mantém a auditoria da API). Uso restrito — LGPD.
/// </summary>
public class RecebimentoModel(AdminApiClient api, AdminSessionService session, ILogger<RecebimentoModel> log) : AdminPageBase(session)
{
    private static readonly TimeZoneInfo Brt = ResolverFusoBrasil();

    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }
    [BindProperty(SupportsGet = true)] public string? Tipo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Busca { get; set; }
    [BindProperty(SupportsGet = true, Name = "page")] public int Pagina { get; set; } = 1;

    public string Titulo { get; private set; } = "Aviso";
    public bool ExigeConfirmacao { get; private set; }
    public int Elegiveis { get; private set; }
    public int Viram { get; private set; }
    public int Confirmaram { get; private set; }
    public int Pendentes => Math.Max(0, Elegiveis - Viram);
    public int TotalEventos { get; private set; }
    public int PageSize { get; } = 25;
    public IReadOnlyList<RecebimentoEventoVM> Eventos { get; private set; } = Array.Empty<RecebimentoEventoVM>();
    public IReadOnlyList<(string Dia, int Total)> Serie { get; private set; } = Array.Empty<(string, int)>();

    public int TotalPaginas => Math.Max(1, (int)Math.Ceiling(TotalEventos / (double)PageSize));
    public int Pct(int n) => Elegiveis > 0 ? (int)Math.Round(100.0 * n / Elegiveis) : 0;
    public string QuandoBrt(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Brt).ToString("dd/MM/yy HH:mm:ss");

    public async Task OnGetAsync()
    {
        Pagina = Math.Max(1, Pagina);
        try
        {
            var q = $"api/admin/banners/{Id}/recebimento?page={Pagina}&pageSize={PageSize}";
            if (!string.IsNullOrWhiteSpace(Tipo)) q += $"&tipo={Uri.EscapeDataString(Tipo)}";
            if (!string.IsNullOrWhiteSpace(Busca)) q += $"&busca={Uri.EscapeDataString(Busca)}";

            var raw = await api.GetRawAsync(q);
            if (!raw.TryGetProperty("data", out var d)) return;

            Titulo = Str(d, "tituloInterno") ?? "Aviso";
            ExigeConfirmacao = Bool(d, "exigeConfirmacao");
            Elegiveis = Int(d, "elegiveis");
            Viram = Int(d, "viram");
            Confirmaram = Int(d, "confirmaram");
            TotalEventos = Int(d, "totalEventos");

            if (d.TryGetProperty("serie", out var s) && s.ValueKind == JsonValueKind.Array)
                Serie = s.EnumerateArray().Select(x => (Str(x, "dia") ?? "", Int(x, "total"))).ToList();

            if (d.TryGetProperty("eventos", out var e) && e.ValueKind == JsonValueKind.Array)
                Eventos = e.EnumerateArray().Select(MapEvento).ToList();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar recebimento {Id}", Id);
            SetErroSeguro(ex, "Console de recebimento");
        }
    }

    /// <summary>Revela o e-mail completo de um usuário (LGPD: a API loga o acesso). GET AJAX da tela.</summary>
    public async Task<IActionResult> OnGetRevelarEmailAsync(Guid usuarioId)
    {
        try
        {
            var raw = await api.GetRawAsync($"api/admin/banners/{Id}/recebimento/{usuarioId}/email");
            var email = raw.TryGetProperty("data", out var d) && d.TryGetProperty("email", out var em)
                ? em.GetString() : null;
            return new JsonResult(new { email });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao revelar e-mail do usuario {Usuario}", usuarioId);
            return new JsonResult(new { email = (string?)null }) { StatusCode = 502 };
        }
    }

    private static RecebimentoEventoVM MapEvento(JsonElement e) => new(
        e.TryGetProperty("usuarioId", out var u) && u.ValueKind == JsonValueKind.String ? u.GetGuid() : Guid.Empty,
        Str(e, "nome") ?? "—",
        Str(e, "empresa"),
        Str(e, "emailMascarado") ?? "—",
        Str(e, "tipo") ?? "—",
        e.TryGetProperty("registradoEmUtc", out var r) && r.ValueKind == JsonValueKind.String && r.TryGetDateTimeOffset(out var dto)
            ? dto.UtcDateTime : DateTime.UtcNow);

    private static string? Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();

    private static int Int(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;

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
}
