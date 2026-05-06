using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Auth;

public class LoginModel(AdminApiClient api, AdminSessionService session, ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    [Required(ErrorMessage = "Informe o e-mail.")]
    [EmailAddress(ErrorMessage = "E-mail inválido.")]
    [StringLength(160, ErrorMessage = "E-mail muito longo.")]
    public string Email { get; set; } = "";

    [BindProperty]
    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(200, MinimumLength = 6, ErrorMessage = "Senha deve ter pelo menos 6 caracteres.")]
    public string Senha { get; set; } = "";

    public string? Erro { get; set; }

    public void OnGet()
    {
        // Se o handler de refresh marcou sessão expirada via cookie, mostra mensagem.
        if (Request.Cookies.ContainsKey("_se_admin"))
        {
            Erro = "Sua sessão expirou. Faça login novamente.";
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
                Erro = "Credenciais inválidas.";
                return Page();
            }

            if (!raw.TryGetProperty("data", out var data))
            {
                Erro = "Resposta inesperada da API.";
                return Page();
            }

            var token = data.TryGetProperty("token", out var t) ? t.GetString() : null;
            var refreshToken = data.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
            var usuario = data.TryGetProperty("usuario", out var u) ? u : default;
            var nivel = usuario.ValueKind != JsonValueKind.Undefined && usuario.TryGetProperty("nivel", out var n)
                ? n.GetString() : null;

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(refreshToken))
            {
                Erro = "Token não recebido.";
                return Page();
            }

            if (!string.Equals(nivel, "SuperAdmin", StringComparison.OrdinalIgnoreCase))
            {
                Erro = "Acesso restrito a administradores.";
                return Page();
            }

            var nome = usuario.TryGetProperty("nome", out var nomeProp) ? nomeProp.GetString() ?? Email : Email;
            session.SetSession(token, refreshToken, nome, Email);

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
