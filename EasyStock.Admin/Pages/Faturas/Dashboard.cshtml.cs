using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Faturas;

/// <summary>
/// Dashboard financeiro: MRR, inadimplência e cohort de receita.
/// Métricas carregadas via <c>api/admin/faturas/metricas?dias=N</c> (padrão 30 dias).
/// </summary>
public class DashboardModel(AdminApiClient api, AdminSessionService session, ILogger<DashboardModel> log) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public int Dias { get; set; } = 30;

    public JsonElement Metricas { get; private set; }
    public string? Erro { get; private set; }
    public bool Carregada => Metricas.ValueKind != JsonValueKind.Undefined;

    public async Task OnGetAsync()
    {
        // Sanitiza
        if (Dias < 7) Dias = 7;
        if (Dias > 365) Dias = 365;

        try
        {
            Metricas = await api.GetAsync<JsonElement>($"api/admin/faturas/metricas?dias={Dias}");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar metricas (dias={Dias})", Dias);
            Erro = "Nao foi possivel carregar as metricas.";
        }
    }

    // Helpers para ler do JsonElement com fallback.
    public decimal Dec(string k) => Metricas.ValueKind != JsonValueKind.Undefined && Metricas.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDecimal() : 0m;
    public int Int(string k) => Metricas.ValueKind != JsonValueKind.Undefined && Metricas.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    public double Dbl(string k) => Metricas.ValueKind != JsonValueKind.Undefined && Metricas.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0d;

    public IEnumerable<JsonElement> TopInadimplentes =>
        Metricas.ValueKind != JsonValueKind.Undefined && Metricas.TryGetProperty("topInadimplentes", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
}
