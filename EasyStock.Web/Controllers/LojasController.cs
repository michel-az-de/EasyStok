using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class LojasController(LojasService svc, SessionService sessionSvc) : BaseController(sessionSvc)
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
    public async Task<IActionResult> Criar(string nome, string? cidade = null, string? telefone = null, string? emoji = null)
    {
        var nomeTrim = nome?.Trim();
        if (string.IsNullOrWhiteSpace(nomeTrim))
        {
            Toast("error", "O nome da loja é obrigatório.");
            return string.IsNullOrEmpty(Session.GetLojaId())
                ? RedirectToAction("SelecionarLoja", "Auth")
                : RedirectToAction(nameof(Index));
        }

        if (nomeTrim.Length > 80)
        {
            Toast("error", "O nome da loja deve ter no máximo 80 caracteres.");
            return string.IsNullOrEmpty(Session.GetLojaId())
                ? RedirectToAction("SelecionarLoja", "Auth")
                : RedirectToAction(nameof(Index));
        }

        var result = await svc.CriarAsync(nomeTrim, cidade?.Trim(), telefone?.Trim());
        // Bloqueio de assinatura ANTES de limite/HasError: sem isso o ramo "sem lojaId"
        // devolve o usuário a SelecionarLoja e o loop de criar loja reaparece (#619).
        if (RedirectIfAssinaturaBloqueada(result) is { } bloqueio) return bloqueio;
        if (RedirectIfLimitReached(result) is { } limitRedirect) return limitRedirect;
        if (HasError(result))
        {
            return string.IsNullOrEmpty(Session.GetLojaId())
                ? RedirectToAction("SelecionarLoja", "Auth")
                : RedirectToAction(nameof(Index));
        }

        Toast("success", $"Loja \"{nomeTrim}\" criada!");

        // Caso o usuário não tenha loja ativa (fluxo de onboarding após login sem
        // lojas vinculadas), auto-seleciona a loja recém-criada para que o
        // BaseController não bloqueie o acesso ao Dashboard.
        if (string.IsNullOrEmpty(Session.GetLojaId()))
        {
            var listaResult = await svc.ListarAsync();
            if (listaResult.Success && listaResult.Data is { Count: > 0 } lojas)
            {
                var nova = lojas.FirstOrDefault(l => string.Equals(l.Nome, nomeTrim, StringComparison.OrdinalIgnoreCase))
                    ?? lojas.Last();
                Session.SetLoja(nova.Id.ToString(), nova.Nome, emoji, nova.EmpresaId.ToString());
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
