using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class CategoriasController(CategoriasService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/categorias")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Categorias";
        ViewBag.ActiveMenuItem = "Categorias";

        var result = await svc.ListarAsync();
        var categorias = result.Success ? result.Data ?? [] : [];

        return View(categorias);
    }

    [HttpPost("/categorias/criar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Criar(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            Toast("error", "O nome da categoria é obrigatório.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.CriarAsync(nome.Trim());
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"Categoria \"{nome.Trim()}\" criada!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/categorias/{id}/editar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Editar(string id, string nome)
    {
        if (string.IsNullOrWhiteSpace(nome))
        {
            Toast("error", "O nome da categoria não pode ser vazio.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.EditarAsync(id, nome.Trim());
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Categoria atualizada!");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/categorias/{id}/limiar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AtualizarLimiar(string id, int? quantidadeMinima, int? quantidadeCritica)
    {
        if (quantidadeMinima.HasValue && quantidadeCritica.HasValue && quantidadeCritica.Value >= quantidadeMinima.Value)
        {
            Toast("error", "A quantidade crítica precisa ser menor que a mínima.");
            return RedirectToAction(nameof(Index));
        }

        var result = await svc.AtualizarLimiarAsync(id, quantidadeMinima, quantidadeCritica);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Limiares da categoria atualizados.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/categorias/{id}/excluir")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Excluir(string id)
    {
        var result = await svc.ExcluirAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Categoria removida.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>JSON endpoint for sidebar dynamic categories</summary>
    [HttpGet("/categorias/listar")]
    public async Task<IActionResult> Listar()
    {
        var result = await svc.ListarAsync();
        if (!result.Success) return Json(Array.Empty<object>());

        var lista = (result.Data ?? []).Select(c => new { id = c.Id, nome = c.Nome });
        return Json(lista);
    }
}
