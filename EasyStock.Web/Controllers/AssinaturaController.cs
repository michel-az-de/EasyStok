using EasyStock.Web.Models.ViewModels.Assinatura;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AssinaturaController(AssinaturaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/assinatura")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Assinatura";
        ViewBag.ActiveMenuItem = "Assinatura";

        var vm = new AssinaturaViewModel();

        var result = await svc.ListarPlanosAsync();
        if (result.Success && result.Data is { } planos)
        {
            vm.Planos = planos.Select(p => new PlanoInfo
            {
                Id = p.Id,
                Nome = p.Nome,
                Descricao = p.Descricao,
                LimiteLojas = p.LimiteLojas,
                LimiteUsuarios = p.LimiteUsuarios,
                LimiteProdutos = p.LimiteProdutos,
                PrecoMensal = p.PrecoMensal
            }).ToList();
        }

        return View(vm);
    }
}
