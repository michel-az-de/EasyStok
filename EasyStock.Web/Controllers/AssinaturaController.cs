using EasyStock.Web.Models.ViewModels.Assinatura;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

public class AssinaturaController(AssinaturaService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/api/assinatura")]
    public async Task<IActionResult> GetStatus()
    {
        var result = await svc.GetStatusAsync();
        if (!result.Success) return NotFound(new { data = (object?)null });
        return Ok(result.Data);
    }

    [HttpGet("/assinatura")]
    public async Task<IActionResult> Index()
    {
        ViewBag.Title = "Assinatura";
        ViewBag.ActiveMenuItem = "Assinatura";

        var vm = new AssinaturaViewModel();

        if (TempData["UpgradeLimite"] is string limiteRecurso)
            vm.LimiteAtingidoRecurso = limiteRecurso;

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
