namespace EasyStock.Admin.Pages.AuditLogs;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public new int Page { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? Acao { get; set; }
    [BindProperty(SupportsGet = true)] public string? De { get; set; }
    [BindProperty(SupportsGet = true)] public string? Ate { get; set; }

    public IEnumerable<JsonElement> Logs { get; private set; } = Enumerable.Empty<JsonElement>();
    public int Total { get; private set; }
    public int TotalPages { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var qs = $"api/admin/audit-admin?page={Page}&pageSize=50";
            if (!string.IsNullOrWhiteSpace(Acao)) qs += $"&acao={Uri.EscapeDataString(Acao)}";
            if (!string.IsNullOrWhiteSpace(De))   qs += $"&de={Uri.EscapeDataString(De)}";
            if (!string.IsNullOrWhiteSpace(Ate))  qs += $"&ate={Uri.EscapeDataString(Ate)}";

            var raw = await api.GetRawAsync(qs);
            Logs = raw.TryGetProperty("data", out var d) ? d.EnumerateArray().ToList() : Enumerable.Empty<JsonElement>();
            if (raw.TryGetProperty("meta", out var meta))
            {
                Total = meta.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
                TotalPages = meta.TryGetProperty("pages", out var p) ? p.GetInt32() : 1;
            }
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar audit logs (page={Page})", Page);
            Erro = ex.Message;
        }
    }
}
