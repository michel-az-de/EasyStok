namespace EasyStock.Admin.Pages.Faturas;

/// <summary>
/// Listagem de faturas com filtros (status, origem, empresa, período, valor,
/// busca). Exportação CSV via <c>OnGetExportCsvAsync</c> reusa os mesmos filtros.
/// </summary>
public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? Origem { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? VencimentoDe { get; set; }
    [BindProperty(SupportsGet = true)] public DateTime? VencimentoAte { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? ValorMin { get; set; }
    [BindProperty(SupportsGet = true)] public decimal? ValorMax { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? EmpresaId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Busca { get; set; }

    public IEnumerable<JsonElement> Faturas { get; private set; } = Enumerable.Empty<JsonElement>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    private static readonly HashSet<string> StatusValidos = new(StringComparer.OrdinalIgnoreCase)
        { "Rascunho", "Emitida", "ParcialmentePaga", "Paga", "Vencida", "Cancelada" };
    private static readonly HashSet<string> OrigemValidas = new(StringComparer.OrdinalIgnoreCase)
        { "Assinatura", "Pedido", "Avulsa" };

    public async Task<IActionResult> OnGetExportCsvAsync(CancellationToken ct)
    {
        // BUG-08 (#636): mesma guarda de intervalo invertido da listagem antes de exportar.
        // Preserva os filtros do operador no redirect (route values -> query string), em vez de
        // cair numa /Faturas com o formulario vazio.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if ((ValorMin is >= 0 && ValorMax is > 0 && ValorMin.Value > ValorMax.Value)
            || (VencimentoDe.HasValue && VencimentoAte.HasValue && VencimentoDe.Value > VencimentoAte.Value))
        {
            SetErro(ValorMin is >= 0 && ValorMax is > 0 && ValorMin.Value > ValorMax.Value
                ? "Intervalo de valor inválido: o valor mínimo é maior que o máximo. Ajuste os campos."
                : "Período inválido: a data inicial é posterior à data final. Ajuste os campos.");
            return RedirectToPage(new
            {
                Status,
                Origem,
                EmpresaId,
                Busca,
                vencimentoDe = VencimentoDe?.ToString("yyyy-MM-dd"),
                vencimentoAte = VencimentoAte?.ToString("yyyy-MM-dd"),
                valorMin = ValorMin?.ToString(inv),
                valorMax = ValorMax?.ToString(inv),
            });
        }

        // Reusa exatamente os mesmos filtros bind-pelo-Get da listagem.
        var qs = new List<string>();
        if (!string.IsNullOrWhiteSpace(Status) && StatusValidos.Contains(Status))
            qs.Add($"status={Uri.EscapeDataString(Status)}");
        if (!string.IsNullOrWhiteSpace(Origem) && OrigemValidas.Contains(Origem))
            qs.Add($"origem={Uri.EscapeDataString(Origem)}");
        if (EmpresaId.HasValue && EmpresaId.Value != Guid.Empty)
            qs.Add($"empresaId={EmpresaId.Value}");
        if (VencimentoDe.HasValue) qs.Add($"vencimentoDe={VencimentoDe.Value:yyyy-MM-dd}");
        if (VencimentoAte.HasValue) qs.Add($"vencimentoAte={VencimentoAte.Value:yyyy-MM-dd}");
        if (ValorMin.HasValue && ValorMin.Value >= 0)
            qs.Add($"valorMin={ValorMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (ValorMax.HasValue && ValorMax.Value > 0)
            qs.Add($"valorMax={ValorMax.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        if (!string.IsNullOrWhiteSpace(Busca))
            qs.Add($"busca={Uri.EscapeDataString(Busca.Trim())}");

        var query = qs.Count > 0 ? "?" + string.Join("&", qs) : "";

        try
        {
            var (bytes, contentType) = await api.GetBytesAsync($"api/admin/faturas/export.csv{query}");
            var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            return File(bytes, contentType ?? "text/csv", $"faturas-{ts}.csv");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao exportar CSV de faturas");
            SetErro($"Falha ao exportar CSV: {ex.Message}");
            return RedirectToPage();
        }
    }

    public async Task OnGetAsync()
    {
        if (Page < 1) Page = 1;
        if (Page > 10000) Page = 10000;

        // BUG-08 (#636): intervalo invertido (min>max) retornaria "0 faturas" silenciosamente.
        // Avisa via toast (TempData -> _Toast) em vez de enganar o operador. So compara quando
        // ambos os limites estao na faixa efetiva do filtro (min>=0 e max>0), igual ao pipeline
        // de query abaixo — evita falso-positivo em entradas degeneradas (ex.: max=0).
        if (ValorMin is >= 0 && ValorMax is > 0 && ValorMin.Value > ValorMax.Value)
        {
            SetErro("Intervalo de valor inválido: o valor mínimo é maior que o máximo. Ajuste os campos.");
            return;
        }
        if (VencimentoDe.HasValue && VencimentoAte.HasValue && VencimentoDe.Value > VencimentoAte.Value)
        {
            SetErro("Período inválido: a data inicial é posterior à data final. Ajuste os campos.");
            return;
        }

        try
        {
            var qs = $"api/admin/faturas?page={Page}&pageSize=25";
            if (!string.IsNullOrWhiteSpace(Status) && StatusValidos.Contains(Status))
                qs += $"&status={Uri.EscapeDataString(Status)}";
            if (!string.IsNullOrWhiteSpace(Origem) && OrigemValidas.Contains(Origem))
                qs += $"&origem={Uri.EscapeDataString(Origem)}";
            if (EmpresaId.HasValue && EmpresaId.Value != Guid.Empty)
                qs += $"&empresaId={EmpresaId.Value}";
            if (VencimentoDe.HasValue)
                qs += $"&vencimentoDe={VencimentoDe.Value:yyyy-MM-dd}";
            if (VencimentoAte.HasValue)
                qs += $"&vencimentoAte={VencimentoAte.Value:yyyy-MM-dd}";
            if (ValorMin.HasValue && ValorMin.Value >= 0)
                qs += $"&valorMin={ValorMin.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (ValorMax.HasValue && ValorMax.Value > 0)
                qs += $"&valorMax={ValorMax.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            if (!string.IsNullOrWhiteSpace(Busca))
                qs += $"&busca={Uri.EscapeDataString(Busca.Trim())}";

            var raw = await api.GetRawAsync(qs);
            Faturas = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
            if (raw.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var t) && t.TryGetInt32(out var tv) ? tv : 0;
                TotalPages = meta.TryGetProperty("pages", out var p) && p.TryGetInt32(out var pv) ? pv : 1;
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar faturas (page={Page}, status={Status})", Page, Status);
            Erro = "Não foi possível carregar a lista de faturas. Tente recarregar a página.";
        }
    }
}
