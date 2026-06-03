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
            var raw = await api.PostRawAsync("api/auth/login", new { email = Email, senha = Senha });

            if (raw.TryGetProperty("error", out _))
            {
                Erro = "E-mail ou senha incorretos. Verifique e tente novamente.";
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

            if (!string.Equals(nivel, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
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

    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "(vazio)";
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return "(invalido)";
        return "***@" + email[(at + 1)..];
    }
}
