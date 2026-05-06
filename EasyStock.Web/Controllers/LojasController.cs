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
        if (RedirectIfLimitReached(result) is { } limitRedirect) return limitRedirect;
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", $"Loja \"{nome.Trim()}\" criada!");

        // Caso o usuário não tenha loja ativa (fluxo de onboarding após login sem
        // lojas vinculadas), auto-seleciona a loja recém-criada para que o
        // BaseController não bloqueie o acesso ao Dashboard.
        if (string.IsNullOrEmpty(session.GetLojaId()))
        {
            var listaResult = await svc.ListarAsync();
            if (listaResult.Success && listaResult.Data is { Count: > 0 } lojas)
            {
                var nova = lojas.FirstOrDefault(l => string.Equals(l.Nome, nome.Trim(), StringComparison.OrdinalIgnoreCase))
                    ?? lojas.Last();
                session.SetLoja(nova.Id.ToString(), nova.Nome, null, nova.EmpresaId.ToString());
                return RedirectToAction("Index", "Dashboard");
            }
        }

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

        Toast("success", "Loja desativada.", $"/lojas/{id}/reativar");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("/lojas/{id}/reativar")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reativar(string id)
    {
        var result = await svc.ReativarAsync(id);
        if (HasError(result)) return RedirectToAction(nameof(Index));

        Toast("success", "Loja reativada.");
        return RedirectToAction(nameof(Index));
    }
}
