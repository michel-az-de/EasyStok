using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.Json;

namespace EasyStock.Admin.Pages.Auth;

public class LoginModel(AdminApiClient api, AdminSessionService session) : PageModel
{
    [BindProperty] public string Email { get; set; } = "";
    [BindProperty] public string Senha { get; set; } = "";
    public string? Erro { get; set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
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
            var usuario = data.TryGetProperty("usuario", out var u) ? u : default;
            var nivel = usuario.ValueKind != JsonValueKind.Undefined && usuario.TryGetProperty("nivel", out var n)
                ? n.GetString() : null;

            if (string.IsNullOrEmpty(token))
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
            session.SetSession(token, nome, Email);

            return RedirectToPage("/Index");
        }
        catch (Exception ex)
        {
            Erro = $"Erro ao conectar à API: {ex.Message}";
            return Page();
        }
    }
}
