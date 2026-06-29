using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace EasyStock.Admin.Pages.Auth;

public class LoginModel(AdminApiClient api, AdminSessionService session, ILogger<LoginModel> logger, IWebHostEnvironment env) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(160, ErrorMessage = "E-mail muito longo.")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(200, MinimumLength = 8, ErrorMessage = "Senha deve ter pelo menos 8 caracteres.")]
    public string Senha { get; set; } = "";

    public string? Erro { get; set; }
    /// <summary>True quando o usuário caiu aqui por sessão expirada (cookie _se_admin).
    /// UI distingue visualmente desse caso (banner amarelo) vs erro de credencial (vermelho).</summary>
    public bool SessaoExpirada { get; private set; }

    public void OnGet()
    {
        // Se o handler de refresh marcou sessão expirada via cookie, mostra mensagem.
        if (Request.Cookies.ContainsKey("_se_admin"))
        {
            SessaoExpirada = true;
            Response.Cookies.Delete("_se_admin");
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            // ModelState.IsValid já popula erros por campo; pega o primeiro pra mostrar no banner.
            Erro = ModelState.Values.SelectMany(v => v.Errors).FirstOrDefault()?.ErrorMessage
                ?? "Preencha os campos corretamente.";
            return Page();
        }

        try
        {
            // Login 1-etapa: o backend resolve o escopo sozinho — SuperAdmin entra direto
            // (sem empresa) e tenant Admin com 1 empresa tem o EmpresaId resolvido no servidor
            // (ResolveEmpresaIdPadrao) e embutido no claim do JWT. O painel é cross-tenant;
            // nao ha seletor de empresa na tela de login (regressao do ADR-0031 fatia 6 revertida).
            var raw = await api.PostRawAsync("api/auth/login", new { email = Email, senha = Senha });

            if (raw.TryGetProperty("error", out var errEl))
            {
                // Distingue throttling (429) de credencial errada — mostrar "senha incorreta" num
                // 429 mandava o usuario re-tentar (piorando o flood) e mascarava a causa real.
                Erro = IsRateLimited(errEl)
                    ? "Muitas tentativas em sequência. Aguarde alguns segundos e tente novamente."
                    : "E-mail ou senha incorretos. Verifique e tente novamente.";
                return Page();
            }

            if (!raw.TryGetProperty("data", out var data))
            {
                Erro = "Não foi possível concluir. Tente novamente em instantes.";
                return Page();
            }

            var token = data.TryGetProperty("token", out var t) ? t.GetString() : null;
            var refreshToken = data.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
            var usuario = data.TryGetProperty("usuario", out var u) ? u : default;
            var nivel = usuario.ValueKind != JsonValueKind.Undefined && usuario.TryGetProperty("nivel", out var n)
                ? n.GetString() : null;

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
            {
                Erro = "Não foi possível concluir. Tente novamente em instantes.";
                return Page();
            }

            // Aceita SuperAdmin (acesso total cross-tenant) ou Admin de tenant. O escopo do
            // Admin vem do claim empresaId do JWT (resolvido no backend), nao de input do front:
            // tenant com 1 empresa entra escopado; com 0/2+ o backend devolve nivel=Visualizador,
            // que cai na rejeicao abaixo. Sem seletor de empresa = sem dead-end de UX.
            var isSuperAdmin = string.Equals(nivel, "SuperAdmin", StringComparison.OrdinalIgnoreCase);
            var isAdmin = string.Equals(nivel, "Admin", StringComparison.OrdinalIgnoreCase);
            if (!isSuperAdmin && !isAdmin)
            {
                Erro = "Acesso restrito a administradores.";
                return Page();
            }

            var nome = usuario.TryGetProperty("nome", out var nomeProp) ? nomeProp.GetString() ?? Email : Email;
            session.SetSession(token, refreshToken, nome, Email);

            // Persiste o refresh token num cookie HttpOnly p/ a sessao sobreviver a
            // deploy/restart (a sessao in-memory e zerada; o AdminSessionRestoreMiddleware
            // restaura a partir daqui). Espelha o cookie _rt do EasyStock.Web.
            Response.Cookies.Append("_rt_admin", refreshToken, new CookieOptions
            {
                HttpOnly = true,
                Secure = !env.IsDevelopment(),
                SameSite = SameSiteMode.Strict,
                Expires = DateTimeOffset.UtcNow.AddDays(30)
            });

            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            // Não logar email cru — LGPD/PII. Loga só o domínio (suficiente pra correlacionar
            // ataques sem expor a identidade do usuário no log).
            logger.LogError(ex, "Falha ao conectar na API durante login admin (emailDomain={Domain})",
                MaskEmail(Email));
            Erro = "Não foi possível concluir o login. Tente novamente em instantes.";
            return Page();
        }
    }

    // Detecta o envelope de rate-limit (429) da API: { error: { code: "RATE_LIMIT_EXCEEDED" } }
    // ou o sintetico do PostRawAsync ("HTTP 429 sem corpo.") quando o 429 vem sem corpo.
    private static bool IsRateLimited(JsonElement errEl)
    {
        if (errEl.ValueKind != JsonValueKind.Object) return false;
        var code = errEl.TryGetProperty("code", out var c) ? c.GetString() : null;
        if (string.Equals(code, "RATE_LIMIT_EXCEEDED", StringComparison.OrdinalIgnoreCase)) return true;
        var msg = errEl.TryGetProperty("message", out var m) ? m.GetString() : null;
        return msg is not null && msg.Contains("429", StringComparison.Ordinal);
    }

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "(invalido)";
        return "***@" + email[(at + 1)..];
    }
}
