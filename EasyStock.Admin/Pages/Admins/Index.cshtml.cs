using EasyStock.Admin.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EasyStock.Admin.Pages.Admins;

public class IndexModel(AdminApiClient api, AdminSessionService session, ILogger<IndexModel> log) : AdminPageBase(session)
{
    public IEnumerable<JsonElement> Admins { get; private set; } = Enumerable.Empty<JsonElement>();
    public string? Erro { get; private set; }
    public string? NovaSenha { get; private set; }
    public string? MensagemSucesso { get; private set; }

    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    public async Task OnGetAsync()
    {
        // Garante Cache-Control: no-store para a página inteira — TempData["NovaSenha"]
        // pode ser renderizada uma vez; não queremos que o browser cacheie a página.
        Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
        Response.Headers["Pragma"] = "no-cache";

        try
        {
            var raw = await api.GetRawAsync("api/admin/admins");
            Admins = raw.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().ToList()
                : Enumerable.Empty<JsonElement>();
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao listar admins");
            Erro = "Não foi possível carregar a lista de administradores.";
        }
    }

    public async Task<IActionResult> OnPostCriarAsync(string nome, string email, string senha)
    {
        var nomeT = (nome ?? "").Trim();
        var emailT = (email ?? "").Trim();
        var senhaT = senha ?? "";

        if (nomeT.Length is < 2 or > 120)
        {
            SetErro("Nome deve ter entre 2 e 120 caracteres.");
            return RedirectToPage();
        }
        if (emailT.Length > 160 || !EmailRegex.IsMatch(emailT))
        {
            SetErro("E-mail inválido.");
            return RedirectToPage();
        }
        if (senhaT.Length is < 8 or > 200)
        {
            SetErro("Senha deve ter entre 8 e 200 caracteres.");
            return RedirectToPage();
        }

        try
        {
            var raw = await api.PostRawAsync("api/admin/admins", new { nome = nomeT, email = emailT, senha = senhaT });
            // PostRawAsync agora não joga em 4xx — caller precisa ler `error`.
            if (raw.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                SetErro($"Falha ao criar admin: {msg ?? "erro desconhecido"}");
            }
            else
            {
                SetSucesso("Admin criado com sucesso.");
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao criar admin");
            SetErro($"Falha ao criar admin: {ex.Message}");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            SetErro("Admin inválido.");
            return RedirectToPage();
        }
        try
        {
            await api.PatchAsync<JsonElement>($"api/admin/admins/{id}/toggle", new { });
            SetSucesso("Status alterado.");
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao alternar admin {AdminId}", id);
            SetErro($"Falha ao alterar status: {ex.Message}");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetSenhaAsync(Guid id)
    {
        if (id == Guid.Empty)
        {
            SetErro("Admin inválido.");
            return RedirectToPage();
        }
        try
        {
            var raw = await api.PostRawAsync($"api/admin/admins/{id}/reset-senha", new { });
            if (raw.TryGetProperty("error", out var err))
            {
                var msg = err.TryGetProperty("message", out var m) ? m.GetString() : null;
                SetErro($"Falha ao resetar senha: {msg ?? "erro desconhecido"}");
            }
            else if (raw.TryGetProperty("data", out var d) && d.TryGetProperty("novaSenha", out var s))
            {
                TempData["NovaSenha"] = s.GetString();
                TempData["ResetAdminId"] = id.ToString();
                SetSucesso("Senha redefinida — nova senha exibida abaixo.");
            }
            else
            {
                SetErro("API não retornou a nova senha. Tente novamente.");
            }
        }
        catch (SessionExpiredException) { throw; }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao resetar senha do admin {AdminId}", id);
            SetErro($"Falha ao resetar senha: {ex.Message}");
        }
        return RedirectToPage();
    }
}
