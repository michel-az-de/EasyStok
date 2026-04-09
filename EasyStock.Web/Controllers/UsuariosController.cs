using System.Text.Json;
using EasyStock.Web.Models.ViewModels.Usuarios;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class UsuariosController(UsuariosService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/usuarios")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Usuários";
        ViewBag.ActiveMenuItem = "Usuarios";

        var result = await svc.ListarAsync();
        var vm = new UsuariosViewModel();

        if (result.Success)
        {
            vm.Usuarios = result.Data!.Select(u => new UsuarioInfo
            {
                Id = u.UsuarioId.ToString(),
                Nome = u.Nome,
                Email = u.Email,
                Role = string.Empty
            }).ToList();

            vm.TotalAdmins = 0;
            vm.TotalColaboradores = vm.Usuarios.Count;
        }

        return View(vm);
    }

    [HttpPost("/usuarios/convidar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convidar(ConvidarUsuarioViewModel vm)
    {
        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(vm.Senha))
        {
            Toast("error", "Preencha todos os campos obrigatórios.");
            return RedirectToAction(nameof(Index));
        }

        var empresaId = ExtractEmpresaId(session.GetToken()) ?? session.GetLojaId();
        if (string.IsNullOrEmpty(empresaId))
        {
            Toast("error", "Não foi possível identificar a empresa. Faça login novamente.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.CriarAsync(empresaId, vm.Nome, vm.Email, vm.Senha);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"Usuário {vm.Nome} criado com sucesso!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/usuarios/{id}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id, string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            Toast("error", "O nome não pode ser vazio.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.EditarAsync(id, nome);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Usuário atualizado!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/usuarios/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        var result = await svc.RemoverAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Usuário removido.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Decodes the JWT payload to extract the empresaId claim, without signature verification.</summary>
    private static string? ExtractEmpresaId(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var parts = token.Split('.');
        if (parts.Length < 2) return null;

        var payload = parts[1];
        switch (payload.Length % 4)
        {
            case 2: payload += "=="; break;
            case 3: payload += "="; break;
        }
        payload = payload.Replace('-', '+').Replace('_', '/');

        try
        {
            var bytes = Convert.FromBase64String(payload);
            using var doc = JsonDocument.Parse(bytes);
            return doc.RootElement.TryGetProperty("empresaId", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch
        {
            return null;
        }
    }
}
