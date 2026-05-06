using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Tenants;

public class DetailModel(AdminApiClient api, AdminSessionService session, IConfiguration config, ILogger<DetailModel> log) : AdminPageBase(session)
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
    public IEnumerable<JsonElement> Features { get; private set; } = Enumerable.Empty<JsonElement>();

    public async Task<IActionResult> OnGetAsync()
    {
        if (Id == Guid.Empty) return NotFound();
        try
        {
            TenantData = await api.GetAsync<JsonElement>($"api/admin/tenants/{Id}");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao carregar tenant {TenantId}", Id);
            Erro = "Não foi possível carregar este tenant. Verifique se ele ainda existe.";
            return Page();
        }

        // Falhas em planos/features são degradação parcial — não bloqueia a página.
        try
        {
            var planosRaw = await api.GetAsync<JsonElement>("api/admin/planos");
            PlanosList = planosRaw.ValueKind == JsonValueKind.Array
                ? planosRaw.EnumerateArray().ToList()
                : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { log.LogWarning(ex, "Falha ao carregar planos no detail do tenant {TenantId}", Id); }

        try
        {
            var featuresRaw = await api.GetRawAsync($"api/admin/tenants/{Id}/features");
            Features = featuresRaw.TryGetProperty("data", out var fd)
                ? fd.EnumerateArray().ToList()
                : Enumerable.Empty<JsonElement>();
        }
        catch (Exception ex) { log.LogWarning(ex, "Falha ao carregar features do tenant {TenantId}", Id); }

        return Page();
    }

    public async Task<IActionResult> OnPostSuspenderAsync(string motivo)
    {
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 3)
        {
            SetErro("Informe um motivo com pelo menos 3 caracteres.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/status",
                new { status = "Suspensa", motivo = motivoT });
            SetSucesso("Tenant suspenso com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao suspender tenant {TenantId}", Id);
            SetErro($"Falha ao suspender tenant: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostReativarAsync()
    {
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/status",
                new { status = "Ativa", motivo = "Reativado pelo admin" });
            SetSucesso("Tenant reativado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao reativar tenant {TenantId}", Id);
            SetErro($"Falha ao reativar tenant: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostTrocarPlanoAsync(Guid planoId)
    {
        if (planoId == Guid.Empty)
        {
            SetErro("Selecione um plano válido.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/plano", new { planoId });
            SetSucesso("Plano alterado com sucesso.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao trocar plano do tenant {TenantId} para {PlanoId}", Id, planoId);
            SetErro($"Falha ao trocar plano: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostImpersonarAsync()
    {
        try
        {
            var result = await api.PostAsync<JsonElement>($"api/admin/tenants/{Id}/impersonate", new { });
            var token = result.TryGetProperty("token", out var t) ? t.GetString() : null;
            if (string.IsNullOrEmpty(token))
            {
                SetErro("Falha ao gerar token de impersonação.");
                return RedirectToPage(new { Id });
            }
            var webUrl = IndexModel.ResolveWebUrl(config);
            if (webUrl is null)
            {
                log.LogError("EasyStockWebUrl ausente ou inválido — impossível fazer handoff de impersonation.");
                SetErro("Configuração de URL do EasyStock.Web inválida. Contate o administrador do sistema.");
                return RedirectToPage(new { Id });
            }
            return Content(IndexModel.BuildHandoffHtml(webUrl, token), "text/html; charset=utf-8");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao impersonar tenant {TenantId}", Id);
            SetErro($"Falha ao impersonar: {ex.Message}");
            return RedirectToPage(new { Id });
        }
    }

    public async Task<IActionResult> OnPostConcederTrialAsync(int diasTrial)
    {
        if (diasTrial is < 1 or > 365)
        {
            SetErro("Dias de trial deve estar entre 1 e 365.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tenants/{Id}/trial", new { diasTrial });
            SetSucesso($"Trial de {diasTrial} dias concedido.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao conceder trial ({Dias}d) ao tenant {TenantId}", diasTrial, Id);
            SetErro($"Falha ao conceder trial: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostAplicarCupomAsync(string codigo)
    {
        var codigoT = (codigo ?? "").Trim();
        if (codigoT.Length is < 3 or > 50)
        {
            SetErro("Código do cupom deve ter entre 3 e 50 caracteres.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PostAsync<JsonElement>($"api/admin/tenants/{Id}/aplicar-cupom", new { codigo = codigoT });
            SetSucesso($"Cupom \"{codigoT}\" aplicado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao aplicar cupom {Codigo} ao tenant {TenantId}", codigoT, Id);
            SetErro($"Falha ao aplicar cupom: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostToggleFeatureAsync(string feature, bool ativo)
    {
        if (string.IsNullOrWhiteSpace(feature) || feature.Length > 80 || !System.Text.RegularExpressions.Regex.IsMatch(feature, "^[a-zA-Z0-9._-]+$"))
        {
            SetErro("Nome de feature inválido.");
            return RedirectToPage(new { Id });
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/tenants/{Id}/features/{Uri.EscapeDataString(feature)}", new { ativo });
            SetSucesso($"Feature \"{feature}\" {(ativo ? "ativada" : "desativada")}.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar feature {Feature} do tenant {TenantId}", feature, Id);
            SetErro($"Falha ao alterar feature: {ex.Message}");
        }
        return RedirectToPage(new { Id });
    }
}
