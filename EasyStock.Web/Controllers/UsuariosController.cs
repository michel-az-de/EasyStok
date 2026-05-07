using EasyStock.Web.Models.ViewModels.Usuarios;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class UsuariosController(UsuariosService svc, LojasService lojasSvc, SessionService sessionSvc) : BaseController(sessionSvc)
{
    [HttpGet("/usuarios")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Usuários";
        ViewBag.ActiveMenuItem = "Usuarios";

        var result = await svc.ListarAsync();
        var lojasResult = await lojasSvc.ListarAsync();
        var vm = new UsuariosViewModel();

        if (lojasResult.Success && lojasResult.Data is not null)
        {
            vm.Lojas = lojasResult.Data
                .Where(l => l.Ativa)
                .Select(l => new Models.Api.Loja(l.Id.ToString(), l.EmpresaId.ToString(), l.Nome, null, "", ""))
                .ToList();
        }

        if (result.Success && result.Data is { } usuarios)
        {
            vm.Usuarios = usuarios.Select(u => new UsuarioInfo
            {
                Id = u.UsuarioId.ToString(),
                Nome = u.Nome,
                Email = u.Email,
                Role = u.Nivel
            }).ToList();

            vm.TotalAdmins = usuarios.Count(u => u.Nivel is "Admin" or "SuperAdmin");
            vm.TotalColaboradores = usuarios.Count(u => u.Nivel is not "Admin" and not "SuperAdmin");
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

        var empresaId = Session.GetEmpresaId();
        if (string.IsNullOrEmpty(empresaId))
        {
            Toast("error", "Não foi possível identificar a empresa. Faça login novamente.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.CriarAsync(empresaId, vm.Nome, vm.Email, vm.Senha, vm.PerfilId, vm.LojaId);
        if (RedirectIfLimitReached(result) is { } limitRedirect) return limitRedirect;
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

    [HttpPost("/usuarios/{id}/perfil")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtribuirPerfil(string id, Guid perfilId, Guid? lojaId)
    {
        var result = await svc.AtribuirPerfilAsync(id, perfilId, lojaId);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Perfil atualizado!");
        return RedirectToAction(nameof(Index));
    }
}
