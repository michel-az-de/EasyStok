using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tenants;

public class DetailModel(AdminApiClient api, AdminSessionService session) : AdminPageBase(session)
{
    [BindProperty(SupportsGet = true)] public Guid Id { get; set; }

    public JsonElement TenantData { get; private set; }
    public string? Erro { get; private set; }
    public string? Mensagem { get; private set; }

    private T Get<T>(string key, T def = default!) where T : struct
    {
        if (TenantData.ValueKind == JsonValueKind.Undefined || !TenantData.TryGetProperty(key, out var v))
            return def;
        var result = v.Deserialize<T>();
        return result is T r ? r : def;
    }

    public JsonElement Empresa => TenantData.ValueKind != JsonValueKind.Undefined && TenantData.TryGetProperty("empresa", out var v) ? v : default;
    public JsonElement Assinatura => TenantData.ValueKind != JsonValueKind.Undefined && TenantData.TryGetProperty("assinatura", out var v) ? v : default;
    public IEnumerable<JsonElement> Lojas => TenantData.ValueKind != JsonValueKind.Undefined && TenantData.TryGetProperty("lojas", out var v) ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> Usuarios => TenantData.ValueKind != JsonValueKind.Undefined && TenantData.TryGetProperty("usuarios", out var v) ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> AuditLogs => TenantData.ValueKind != JsonValueKind.Undefined && TenantData.TryGetProperty("auditLogRecentes", out var v) ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
    public IEnumerable<JsonElement> PlanosList { get; private set; } = Enumerable.Empty<JsonElement>();

    public async Task OnGetAsync()
    {
        try
        {
            TenantData = await api.GetAsync<JsonElement>($"api/admin/tenants/{Id}");
            var planosRaw = await api.GetAsync<JsonElement>("api/admin/planos");
            PlanosList = planosRaw.ValueKind == JsonValueKind.Array
                ? planosRaw.EnumerateArray()
                : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { Erro = ex.Message; }
    }

    public async Task<IActionResult> OnPostSuspenderAsync(string motivo)
    {
        await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/status",
            new { status = "Suspensa", motivo });
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostReativarAsync()
    {
        await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/status",
            new { status = "Ativa", motivo = "Reativado pelo admin" });
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostTrocarPlanoAsync(Guid planoId)
    {
        await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/plano", new { planoId });
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostImpersonarAsync()
    {
        var result = await api.PostAsync<JsonElement>($"api/admin/tenants/{Id}/impersonate", new { });
        var token = result.TryGetProperty("token", out var t) ? t.GetString() : null;
        return Redirect($"http://localhost:5000/auth/impersonate?token={Uri.EscapeDataString(token ?? "")}");
    }
}
