using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages;

public class IndexModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    public JsonElement DashData { get; private set; }
    public string? Erro { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            DashData = await api.GetAsync<JsonElement>("api/admin/dashboard");
        }
        catch (Exception ex)
        {
            Erro = ex.Message;
        }
    }

    private int Get(string key) =>
        DashData.ValueKind != JsonValueKind.Undefined && DashData.TryGetProperty(key, out var v) ? v.GetInt32() : 0;

    public int TotalTenants => Get("totalTenants");
    public int TenantsAtivos => Get("tenantsAtivos");
    public int TenantsSuspensos => Get("tenantsSuspensos");
    public int TenantsNovos => Get("tenantsNovosUltimos30Dias");
    public int TicketsAbertos => Get("ticketsAbertos");
    public int TicketsCriticos => Get("ticketsCriticos");
    public int TicketsEmAtendimento => Get("ticketsEmAtendimento");
    public int TotalUsuariosAtivos => Get("totalUsuariosAtivos");
    public int Logins24h => Get("logins24h");

    public decimal ReceitaMensal =>
        DashData.ValueKind != JsonValueKind.Undefined && DashData.TryGetProperty("receitaMensalEstimada", out var v)
            ? v.GetDecimal() : 0m;

    public IEnumerable<JsonElement> UltimosTicketsCriticos =>
        DashData.ValueKind != JsonValueKind.Undefined && DashData.TryGetProperty("ultimosTicketsCriticos", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();

    public IEnumerable<JsonElement> TenantsRecentes =>
        DashData.ValueKind != JsonValueKind.Undefined && DashData.TryGetProperty("tenantsRecentes", out var v)
            ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
}
