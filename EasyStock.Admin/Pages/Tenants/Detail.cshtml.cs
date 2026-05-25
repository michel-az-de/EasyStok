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

    /// <summary>
    /// Senha temporária do usuário recém-criado (cadastro de tenant ou novo usuário no tenant).
    /// Sobrevive a 1 redirect via TempData. Banner exibe e some no próximo navigate.
    /// </summary>
    public string? NovaSenhaTemporaria => TempData["NovaSenhaTemporaria"] as string;
    public string? NovoUsuarioNome => TempData["NovoUsuarioNome"] as string;
    public string? NovoUsuarioEmail => TempData["NovoUsuarioEmail"] as string;

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

    /// <summary>
    /// F4 — Handler AJAX para o card "Sincronização Mobile" no Detail.
    /// Proxy direto pro endpoint API GetMobileSyncHealth. Retorna JSON com
    /// counts de mobile_* sem erp_*_id pra detectar gap de sync.
    /// </summary>
    public async Task<IActionResult> OnGetMobileSyncHealthAsync()
    {
        try
        {
            var data = await api.GetAsync<JsonElement>($"api/admin/tenants/{Id}/mobile-sync-health");
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Falha ao carregar mobile-sync-health do tenant {TenantId}", Id);
            return new JsonResult(new { error = ex.Message });
        }
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

    /// <summary>
    /// Cria um novo usuário dentro do tenant atual. Senha é gerada server-side e
    /// retorna 1 vez via TempData → banner dourado na próxima carga da página.
    /// </summary>
    public async Task<IActionResult> OnPostCriarUsuarioAsync(
        string motivo,
        string nome,
        string email,
        string nivel,
        bool enviarEmail = true)
    {
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) { SetErro("Justificativa obrigatória (mínimo 10 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        if (string.IsNullOrWhiteSpace(nome) || nome.Trim().Length < 2) { SetErro("Nome é obrigatório (mín. 2 caracteres)."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        if (string.IsNullOrWhiteSpace(email)) { SetErro("E-mail é obrigatório."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var nivelT = (nivel ?? "Operador").Trim();

        try
        {
            var resp = await api.PostAsync<JsonElement>("api/admin/usuarios-tenant", new
            {
                motivo = motivoT,
                tenantId = Id,
                nome = nome.Trim(),
                email = email.Trim(),
                nivel = nivelT,
                enviarEmail
            });

            var nomeFinal = resp.TryGetProperty("nome", out var np) ? np.GetString() : nome.Trim();
            var emailFinal = resp.TryGetProperty("email", out var ep) ? ep.GetString() : email.Trim();
            var senha = resp.TryGetProperty("senhaTemporaria", out var sp) ? sp.GetString() : null;
            var emailEnviado = resp.TryGetProperty("emailEnviado", out var eep) && eep.GetBoolean();

            if (!string.IsNullOrEmpty(senha))
            {
                TempData["NovaSenhaTemporaria"] = senha;
                TempData["NovoUsuarioNome"] = nomeFinal;
                TempData["NovoUsuarioEmail"] = emailFinal;
            }

            SetSucesso(emailEnviado
                ? $"{nomeFinal} adicionado ao cliente. Senha temporária enviada para {emailFinal}."
                : $"{nomeFinal} adicionado ao cliente. Anote a senha temporária — o e-mail NÃO foi enviado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar usuário no tenant {TenantId}", Id);
            SetErro($"Falha ao criar usuário: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "usuarios" });
    }

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
        if (userId == Guid.Empty)
            return new JsonResult(new { error = "userId inválido" }) { StatusCode = 400 };
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

    // ─────────────────── Atividade unificada (P1 slice 1) ───────────────────

    public async Task<IActionResult> OnGetAtividadeAsync(
        int page = 1,
        string? tipo = null,
        DateTime? from = null,
        DateTime? to = null,
        Guid? usuarioId = null,
        string? search = null)
    {
        try
        {
            var qs = $"api/admin/clientes/{Id}/atividade?page={page}&pageSize=20";
            if (!string.IsNullOrWhiteSpace(tipo)) qs += $"&tipo={Uri.EscapeDataString(tipo)}";
            if (from.HasValue) qs += $"&from={Uri.EscapeDataString(from.Value.ToString("o"))}";
            if (to.HasValue) qs += $"&to={Uri.EscapeDataString(to.Value.ToString("o"))}";
            if (usuarioId.HasValue && usuarioId.Value != Guid.Empty) qs += $"&usuarioId={usuarioId.Value}";
            if (!string.IsNullOrWhiteSpace(search)) qs += $"&search={Uri.EscapeDataString(search)}";

            var data = await api.GetRawAsync(qs);
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar atividade do tenant {TenantId}", Id);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    // ─────────────────── PII unmask just-in-time (P1 slice 1) ───────────────────

    public async Task<IActionResult> OnGetPiiUsuarioAsync(Guid userId, string campo, string motivo)
    {
        if (userId == Guid.Empty) return new JsonResult(new { error = "userId inválido" }) { StatusCode = 400 };
        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 10)
            return new JsonResult(new { error = "Motivo obrigatório (≥10 chars)" }) { StatusCode = 400 };

        try
        {
            var data = await api.GetAsync<JsonElement>(
                $"api/admin/clientes/{Id}/usuario-pii/{userId}?campo={Uri.EscapeDataString(campo ?? "email")}&motivo={Uri.EscapeDataString(motivo)}");
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao desmascarar PII {Campo} do usuário {UserId}", campo, userId);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    // ─────────────────── LGPD (P1 slice 2) ───────────────────

    public async Task<IActionResult> OnPostAnonimizarUsuarioAsync(Guid userId, string motivo, string confirmacaoEmail)
    {
        if (userId == Guid.Empty) { SetErro("Usuário inválido."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 20) { SetErro("Justificativa obrigatória (≥20 caracteres) — anonimização é irreversível."); return RedirectToPage(new { Id, tab = "usuarios" }); }
        if (string.IsNullOrWhiteSpace(confirmacaoEmail)) { SetErro("Confirmação de email é obrigatória."); return RedirectToPage(new { Id, tab = "usuarios" }); }

        try
        {
            var resp = await api.PostAsync<JsonElement>(
                $"api/admin/clientes/{Id}/usuarios/{userId}/anonimizar",
                new { motivo = motivoT, confirmacaoEmail = confirmacaoEmail.Trim() });
            var msg = resp.TryGetProperty("mensagem", out var mp) ? mp.GetString() : "Usuário anonimizado.";
            SetSucesso(msg ?? "Usuário anonimizado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao anonimizar usuário {UserId} do tenant {TenantId}", userId, Id);
            SetErro($"Falha ao anonimizar: {ex.Message}");
        }
        return RedirectToPage(new { Id, tab = "lgpd" });
    }

    public async Task<IActionResult> OnGetExportarDadosAsync(string motivo)
    {
        var motivoT = (motivo ?? "").Trim();
        if (motivoT.Length < 10) return BadRequest(new { error = "Motivo obrigatório (≥10 caracteres)." });

        try
        {
            // O endpoint da API devolve File JSON. ApiClient.GetBytesAsync busca como bytes
            // sem tentar deserializar — caminho ideal pra download cru.
            var (bytes, ct) = await api.GetBytesAsync(
                $"api/admin/clientes/{Id}/exportar?motivo={Uri.EscapeDataString(motivoT)}");
            var fileName = $"easystock-export-tenant-{Id:N}-{DateTime.UtcNow:yyyyMMddHHmmss}.json";
            return File(bytes, ct, fileName);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao exportar dados do tenant {TenantId}", Id);
            SetErro($"Falha ao exportar: {ex.Message}");
            return RedirectToPage(new { Id, tab = "lgpd" });
        }
    }

    public async Task<IActionResult> OnGetHistoricoLgpdAsync(int page = 1)
    {
        try
        {
            var data = await api.GetRawAsync($"api/admin/clientes/{Id}/lgpd/historico?page={page}&pageSize=20");
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar histórico LGPD do tenant {TenantId}", Id);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    // ─────────────────── Notas internas (P3) ───────────────────

    public async Task<IActionResult> OnGetNotasAsync()
    {
        try
        {
            var data = await api.GetRawAsync($"api/admin/clientes/{Id}/notas");
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar notas do tenant {TenantId}", Id);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostCriarNotaAsync(string texto, string tipo)
    {
        var textoT = (texto ?? "").Trim();
        if (textoT.Length is < 3 or > 2000)
            return new JsonResult(new { error = "Texto entre 3 e 2000 caracteres." }) { StatusCode = 400 };
        var tipoT = (tipo ?? "Info").Trim();
        if (tipoT is not ("Info" or "Alerta" or "Escalonamento"))
            return new JsonResult(new { error = "Tipo inválido." }) { StatusCode = 400 };

        try
        {
            var data = await api.PostAsync<JsonElement>($"api/admin/clientes/{Id}/notas",
                new { texto = textoT, tipo = tipoT });
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar nota no tenant {TenantId}", Id);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostAtualizarNotaAsync(Guid notaId, string texto, string tipo)
    {
        if (notaId == Guid.Empty) return new JsonResult(new { error = "Nota inválida." }) { StatusCode = 400 };
        var textoT = (texto ?? "").Trim();
        if (textoT.Length is < 3 or > 2000)
            return new JsonResult(new { error = "Texto entre 3 e 2000 caracteres." }) { StatusCode = 400 };
        var tipoT = (tipo ?? "Info").Trim();

        try
        {
            var data = await api.PatchAsync<JsonElement>($"api/admin/clientes/{Id}/notas/{notaId}",
                new { texto = textoT, tipo = tipoT });
            return new JsonResult(data);
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao atualizar nota {NotaId}", notaId);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
    }

    public async Task<IActionResult> OnPostExcluirNotaAsync(Guid notaId)
    {
        if (notaId == Guid.Empty) return new JsonResult(new { error = "Nota inválida." }) { StatusCode = 400 };
        try
        {
            await api.DeleteAsync($"api/admin/clientes/{Id}/notas/{notaId}");
            return new JsonResult(new { ok = true });
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao excluir nota {NotaId}", notaId);
            return new JsonResult(new { error = ex.Message }) { StatusCode = 502 };
        }
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
