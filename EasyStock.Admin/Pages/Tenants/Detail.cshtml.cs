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

    // ─────────────────────── Ações sobre usuário do tenant (P0) ───────────────────────
    // Endpoints que cobrem 80% dos chamados de suporte: reset senha, forçar logout, ver sessões.
    // Todos exigem `motivo` (≥10 chars) auditado no AdminAuditLog.

    public async Task<IActionResult> OnPostResetSenhaUsuarioAsync(Guid userId, string motivo)
    {
        if (userId == Guid.Empty) { SetErro("Usuário inválido."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }

        try
        {
            var resp = await api.PostAsync<JsonElement>(
                $"api/admin/usuarios-tenant/{userId}/reset-senha",
                new { motivo = motivoT, enviarPorEmail = true });
            var emailEnviado = resp.TryGetProperty("emailEnviado", out var eep) && eep.GetBoolean();
            var sessoesRevogadas = resp.TryGetProperty("sessoesRevogadas", out var srp) && srp.TryGetInt32(out var sr) ? sr : 0;
            SetSucesso(emailEnviado
                ? $"Senha resetada — email enviado ao usuário ({sessoesRevogadas} sessão(ões) revogada(s))."
                : $"Senha resetada, mas o email NÃO foi enviado. Informe o usuário pelo canal oficial. ({sessoesRevogadas} sessão(ões) revogada(s)).");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao resetar senha do usuário {UserId} do tenant {TenantId}", userId, Id);
            SetErro($"Falha ao resetar senha: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "usuarios" });
    }

    public async Task<IActionResult> OnPostForcarLogoutUsuarioAsync(Guid userId, string motivo)
    {
        if (userId == Guid.Empty) { SetErro("Usuário inválido."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }

        try
        {
            var resp = await api.PostAsync<JsonElement>(
                $"api/admin/usuarios-tenant/{userId}/forcar-logout",
                new { motivo = motivoT });
            var revogadas = resp.TryGetProperty("sessoesRevogadas", out var srp) && srp.TryGetInt32(out var sr) ? sr : 0;
            SetSucesso(revogadas > 0
                ? $"{revogadas} sessão(ões) ativa(s) revogada(s)."
                : "Usuário não tinha sessões ativas — nenhuma alteração.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao forçar logout do usuário {UserId} do tenant {TenantId}", userId, Id);
            SetErro($"Falha ao forçar logout: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "usuarios" });
    }

    public async Task<IActionResult> OnGetSessoesUsuarioAsync(Guid userId)
    {
        if (userId == Guid.Empty) return BadRequest(new { error = "userId inválido" });
        try
        {
            var data = await api.GetAsync<JsonElement>($"api/admin/usuarios-tenant/{userId}/sessoes");
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar sessões do usuário {UserId}", userId);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostAtualizarUsuarioAsync(Guid userId, string motivo, string? nome, string? email)
    {
        if (userId == Guid.Empty) { SetErro("Usuário inválido."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }

        try
        {
            var resp = await api.PatchAsync<JsonElement>(
                $"api/admin/usuarios-tenant/{userId}",
                new { motivo = motivoT, nome = nome?.Trim(), email = email?.Trim() });
            var alterado = resp.TryGetProperty("alterado", out var ap) && ap.GetBoolean();
            var msg = resp.TryGetProperty("mensagem", out var mp) ? mp.GetString() : null;
            SetSucesso(alterado ? (msg ?? "Usuário atualizado.") : "Nenhuma alteração — valores idênticos.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao atualizar usuário {UserId} do tenant {TenantId}", userId, Id);
            SetErro($"Falha ao atualizar usuário: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "usuarios" });
    }

    public async Task<IActionResult> OnPostDesativarUsuarioAsync(Guid userId, string motivo)
    {
        if (userId == Guid.Empty) { SetErro("Usuário inválido."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }

        try
        {
            var resp = await api.PostAsync<JsonElement>(
                $"api/admin/usuarios-tenant/{userId}/desativar",
                new { motivo = motivoT });
            var sessoes = resp.TryGetProperty("sessoesRevogadas", out var srp) && srp.TryGetInt32(out var sr) ? sr : 0;
            SetSucesso(sessoes > 0
                ? $"Usuário desativado e {sessoes} sessão(ões) ativa(s) revogada(s)."
                : "Usuário desativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao desativar usuário {UserId} do tenant {TenantId}", userId, Id);
            SetErro($"Falha ao desativar: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "usuarios" });
    }

    public async Task<IActionResult> OnPostReativarUsuarioAsync(Guid userId, string motivo)
    {
        if (userId == Guid.Empty) { SetErro("Usuário inválido."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }

        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/usuarios-tenant/{userId}/reativar",
                new { motivo = motivoT });
            SetSucesso("Usuário reativado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao reativar usuário {UserId} do tenant {TenantId}", userId, Id);
            SetErro($"Falha ao reativar: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "usuarios" });
    }

    // ─────────────────────── Lojas (CRUD via admin) ───────────────────────

    public async Task<IActionResult> OnPostCriarLojaAsync(string motivo, string nome, string? descricao, string? documento, string? endereco, string? telefone)
    {
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "lojas" }); }
        if (string.IsNullOrWhiteSpace(nome)) { SetErro("Nome da loja é obrigatório."); return RedirectToPage(new { Id, tab = "lojas" }); }

        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/clientes/{Id}/lojas",
                new { motivo = motivoT, nome = nome.Trim(), descricao, documento, endereco, telefone });
            SetSucesso($"Loja \"{nome.Trim()}\" criada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar loja para tenant {TenantId}", Id);
            SetErro($"Falha ao criar loja: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "lojas" });
    }

    public async Task<IActionResult> OnPostAtualizarLojaAsync(Guid lojaId, string motivo, string nome, string? descricao, string? documento, string? endereco, string? telefone)
    {
        if (lojaId == Guid.Empty) { SetErro("Loja inválida."); return RedirectToPage(new { Id, tab = "lojas" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "lojas" }); }
        if (string.IsNullOrWhiteSpace(nome)) { SetErro("Nome da loja é obrigatório."); return RedirectToPage(new { Id, tab = "lojas" }); }

        try
        {
            var resp = await api.PatchAsync<JsonElement>(
                $"api/admin/clientes/{Id}/lojas/{lojaId}",
                new { motivo = motivoT, nome = nome.Trim(), descricao, documento, endereco, telefone });
            var alterado = resp.TryGetProperty("alterado", out var ap) && ap.GetBoolean();
            SetSucesso(alterado ? "Loja atualizada." : "Nenhuma alteração efetiva.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao atualizar loja {LojaId} do tenant {TenantId}", lojaId, Id);
            SetErro($"Falha ao atualizar loja: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "lojas" });
    }

    public async Task<IActionResult> OnPostToggleLojaAsync(Guid lojaId, string motivo, bool ativa)
    {
        if (lojaId == Guid.Empty) { SetErro("Loja inválida."); return RedirectToPage(new { Id, tab = "lojas" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "lojas" }); }

        try
        {
            await api.PostAsync<JsonElement>(
                $"api/admin/clientes/{Id}/lojas/{lojaId}/toggle",
                new { motivo = motivoT, ativa });
            SetSucesso(ativa ? "Loja reativada." : "Loja desativada.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar loja {LojaId} do tenant {TenantId}", lojaId, Id);
            SetErro($"Falha ao alternar loja: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "lojas" });
    }
}
