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
                Id = u.Id,
                Nome = u.Nome,
                Email = u.Email,
                Role = u.Role
            }).ToList();

            vm.TotalAdmins = vm.Usuarios.Count(u => u.Role is "Admin" or "Owner");
            vm.TotalColaboradores = vm.Usuarios.Count(u => u.Role == "Operador");
        }

        return View(vm);
    }

    [HttpPost("/usuarios/convidar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Convidar(ConvidarUsuarioViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            Toast("error", "Preencha todos os campos obrigatórios.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.ConvidarAsync(vm.Nome, vm.Email, vm.Role, vm.LojaIds);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"Convite enviado para {vm.Email}!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/usuarios/{id}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id, string role)
    {
        var result = await svc.EditarAsync(id, new { role });
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
}
