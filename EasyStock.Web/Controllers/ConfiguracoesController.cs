using EasyStock.Web.Models.ViewModels.Configuracoes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ConfiguracoesController(ConfiguracoesService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/configuracoes")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Configurações";
        ViewBag.ActiveMenuItem = "Configuracoes";

        var result = await svc.ObterAsync();
        if (!result.Success || result.Data is null)
            return View(new ConfiguracoesViewModel());

        var cfg = result.Data;
        var vm = new ConfiguracoesViewModel
        {
            DiasAlertaValidade = cfg.DiasAlertaValidade,
            DiasAlertaParado = cfg.DiasAlertaParado,
            QtyMinPadrao = cfg.QtyMinPadrao,
            QtyCritPadrao = cfg.QtyCritPadrao,
            NotifEstoqueCritico = cfg.NotifEstoqueCritico,
            NotifValidade = cfg.NotifValidade,
            NotifParado = cfg.NotifParado,
            NotifReposicao = cfg.NotifReposicao,
            Fifo = cfg.Fifo,
            KdsHabilitado = cfg.KdsHabilitado
        };

        return View(vm);
    }

    [HttpPost("/configuracoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar(ConfiguracoesViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Configurações";
            ViewBag.ActiveMenuItem = "Configuracoes";
            return View("Index", vm);
        }

        var result = await svc.SalvarAsync(vm);
        if (HasError(result))
        {
            ViewBag.Title = "Configurações";
            ViewBag.ActiveMenuItem = "Configuracoes";
            return View("Index", vm);
        }

        Toast("success", "Configurações salvas com sucesso!");
        return RedirectToAction(nameof(Index));
    }
}
