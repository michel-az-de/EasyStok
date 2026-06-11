using EasyStock.Web.Models.ViewModels.ConfiguracaoFiscal;
using EasyStock.Web.Models.ViewModels.Configuracoes;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class ConfiguracoesController(
    ConfiguracoesService svc,
    ConfiguracaoFiscalService fiscalSvc,
    SessionService session) : BaseController(session)
{
    [HttpGet("/configuracoes")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Configurações";
        ViewBag.ActiveMenuItem = "Configuracoes";
        return View(await MontarIndexVmAsync());
    }

    [HttpPost("/configuracoes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar(ConfiguracoesViewModel vm)
    {
        ViewBag.Title = "Configurações";
        ViewBag.ActiveMenuItem = "Configuracoes";

        if (!ModelState.IsValid)
            return View("Index", await MontarIndexVmAsync(vm));

        var result = await svc.SalvarAsync(vm);
        if (HasError(result))
            return View("Index", await MontarIndexVmAsync(vm));

        Toast("success", "Configurações salvas com sucesso!");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Compõe a página (aba Geral + aba Fiscal) de forma EAGER (PATCH-2): usado pelo
    /// GET e pelo caminho de erro do POST, p/ a aba fiscal nunca renderizar sem VM.
    /// Quando <paramref name="geral"/> vem (re-render de erro), preserva o que o usuário
    /// digitou; senão busca do backend.
    /// </summary>
    private async Task<ConfiguracoesPageViewModel> MontarIndexVmAsync(ConfiguracoesViewModel? geral = null)
    {
        if (geral is null)
        {
            var cfg = await svc.ObterAsync();
            geral = cfg.Success && cfg.Data is { } d
                ? new ConfiguracoesViewModel
                {
                    DiasAlertaValidade = d.DiasAlertaValidade,
                    DiasAlertaParado = d.DiasAlertaParado,
                    QtyMinPadrao = d.QtyMinPadrao,
                    QtyCritPadrao = d.QtyCritPadrao,
                    NotifEstoqueCritico = d.NotifEstoqueCritico,
                    NotifValidade = d.NotifValidade,
                    NotifParado = d.NotifParado,
                    NotifReposicao = d.NotifReposicao,
                    Fifo = d.Fifo,
                    KdsHabilitado = d.KdsHabilitado,
                }
                : new ConfiguracoesViewModel();
        }

        var fiscalResult = await fiscalSvc.ObterAsync();
        var fiscal = (fiscalResult.Success ? fiscalResult.Data : null) ?? new ConfiguracaoFiscalViewModel();

        return new ConfiguracoesPageViewModel(geral, fiscal);
    }
}
