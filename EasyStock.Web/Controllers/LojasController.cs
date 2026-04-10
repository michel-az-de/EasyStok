using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class LojasController(LojasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/lojas")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Lojas";
        ViewBag.ActiveMenuItem = "Lojas";

        var result = await svc.ListarAsync();
        var lojas = result.Success ? result.Data ?? [] : [];

        return View(lojas);
    }

    [HttpPost("/lojas/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            Toast("error", "O nome da loja é obrigatório.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.CriarAsync(nome.Trim());
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"Loja \"{nome.Trim()}\" criada!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/lojas/{id}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id, string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            Toast("error", "O nome da loja não pode ser vazio.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.EditarAsync(id, nome.Trim());
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Loja atualizada!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/lojas/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        var result = await svc.ExcluirAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Loja desativada.");
        return RedirectToAction(nameof(Index));
    }
}
