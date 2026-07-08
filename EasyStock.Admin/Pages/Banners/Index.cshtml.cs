namespace EasyStock.Admin.Pages.Banners;

/// <summary>Estado efetivo do aviso, derivado de <c>Ativo</c> + janela vs. agora (UTC).</summary>
public enum BannerEstado { NoAr, Agendado, Expirado, Inativo }

/// <summary>Linha da lista do Quadro de avisos — já com estado e janela prontos para exibir.</summary>
public sealed record BannerLinha(
    Guid Id,
    string Titulo,
    bool EhImagem,
    string? ImagemUrl,
    BannerEstado Estado,
    string Janela,
    int Prioridade,
    bool ExigeConfirmacao,
    bool Notifica,
    bool Unica);

/// <summary>Listagem e ações rápidas do Quadro de avisos (#869). SuperAdmin apenas.</summary>
public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    private static readonly TimeZoneInfo Brt = ResolverFusoBrasil();

    public IReadOnlyList<BannerLinha> Itens { get; private set; } = Array.Empty<BannerLinha>();
    public int Total { get; private set; }
    // 'new': esconde deliberadamente PageModel.Page() (metodo base); aqui Page e o
    // numero da pagina atual. Sem 'new' o compilador emite CS0108, que vira erro sob
    // -warnaserror e quebrava o Build da CI.
    public new int Page { get; private set; } = 1;
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
                Itens = data.EnumerateArray().Select(MapearLinha).ToList();
            if (raw.TryGetProperty("meta", out var meta) && meta.TryGetProperty("total", out var t))
                Total = t.GetInt32();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar avisos");
            SetErroSeguro(ex, "Listagem do quadro de avisos");
        }
    }

    private static BannerLinha MapearLinha(JsonElement b)
    {
        var ativo = Bool(b, "ativo");
        var inicio = UtcOpt(b, "inicioEm");
        var fim = UtcOpt(b, "fimEm");
        var agora = DateTime.UtcNow;

        var estado = !ativo ? BannerEstado.Inativo
            : inicio is { } ini && ini > agora ? BannerEstado.Agendado
            : fim is { } f && f <= agora ? BannerEstado.Expirado
            : BannerEstado.NoAr;

        return new BannerLinha(
            Id: b.GetProperty("id").GetGuid(),
            Titulo: Str(b, "tituloInterno") ?? "(sem título)",
            EhImagem: !string.Equals(Str(b, "tipo"), "Mensagem", StringComparison.OrdinalIgnoreCase),
            ImagemUrl: Str(b, "imagemUrl"),
            Estado: estado,
            Janela: FormatarJanela(inicio, fim),
            Prioridade: b.TryGetProperty("prioridade", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : 0,
            ExigeConfirmacao: Bool(b, "exigeConfirmacao"),
            Notifica: Bool(b, "notificarAoPublicar"),
            Unica: Bool(b, "visualizacaoUnica"));
    }

    private static string FormatarJanela(DateTime? inicioUtc, DateTime? fimUtc)
    {
        string Fmt(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Brt).ToString("dd/MM/yy HH:mm");
        return (inicioUtc, fimUtc) switch
        {
            (null, null) => "Sempre visível",
            ({ } i, null) => $"A partir de {Fmt(i)}",
            (null, { } f) => $"Até {Fmt(f)}",
            ({ } i, { } f) => $"{Fmt(i)} → {Fmt(f)}"
        };
    }

    public Task<IActionResult> OnPostAtivarAsync(Guid id) => AcaoAsync($"api/admin/banners/{id}/ativar", "Aviso ativado.");
    public Task<IActionResult> OnPostDesativarAsync(Guid id) => AcaoAsync($"api/admin/banners/{id}/desativar", "Aviso desativado.");

    public async Task<IActionResult> OnPostExcluirAsync(Guid id)
    {
        try
        {
            await api.DeleteAsync($"api/admin/banners/{id}");
            SetSucesso("Aviso excluído.");
        }
        catch (ApiException ex)
        {
            // 409 (aviso com confirmações) chega como ApiException com a mensagem amigável.
            SetErro(ex.Message);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao excluir aviso {Id}", id);
            SetErroSeguro(ex, "Exclusão de aviso");
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
            SetErroSeguro(ex, "Ação em aviso");
        }
        return RedirectToPage("/Banners/Index");
    }

    private static string? Str(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && (v.ValueKind == JsonValueKind.True || v.ValueKind == JsonValueKind.False) && v.GetBoolean();

    private static DateTime? UtcOpt(JsonElement e, string prop)
        => e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var dto)
            ? dto.UtcDateTime : null;

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
