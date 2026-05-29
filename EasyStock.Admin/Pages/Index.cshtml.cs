namespace EasyStock.Admin.Pages;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public JsonElement DashData { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            DashData = await api.GetAsync<JsonElement>("api/admin/dashboard");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar dashboard admin");
            Erro = "Não foi possível carregar o dashboard. Tente recarregar a página.";
        }
    }

    // TryGetInt32/Decimal evitam crash se a API mandar null, string ou número fora do range.
    // Sempre devolvem 0 — counter "0" é melhor que página inteira em 500.
    private int GetInt(string key)
    {
        if (DashData.ValueKind == JsonValueKind.Undefined) return 0;
        if (!DashData.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : 0;
    }

    private decimal GetDec(string key)
    {
        if (DashData.ValueKind == JsonValueKind.Undefined) return 0m;
        if (!DashData.TryGetProperty(key, out var v)) return 0m;
        return v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var d) ? d : 0m;
    }

    public int TotalTenants => GetInt("totalTenants");
    public int TenantsAtivos => GetInt("tenantsAtivos");
    public int TenantsSuspensos => GetInt("tenantsSuspensos");
    public int TenantsNovos => GetInt("tenantsNovosUltimos30Dias");
    public int TicketsAbertos => GetInt("ticketsAbertos");
    public int TicketsCriticos => GetInt("ticketsCriticos");
    public int TicketsEmAtendimento => GetInt("ticketsEmAtendimento");
    public int TotalUsuariosAtivos => GetInt("totalUsuariosAtivos");
    public int Logins24h => GetInt("logins24h");

    public decimal ReceitaMensal => GetDec("receitaMensalEstimada");

    public IEnumerable<JsonElement> UltimosTicketsCriticos =>
        DashData.ValueKind != JsonValueKind.Undefined && DashData.TryGetProperty("ultimosTicketsCriticos", out var v)
        && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public IEnumerable<JsonElement> TenantsRecentes =>
        DashData.ValueKind != JsonValueKind.Undefined && DashData.TryGetProperty("tenantsRecentes", out var v)
        && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
