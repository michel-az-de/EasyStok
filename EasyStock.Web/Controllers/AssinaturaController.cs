using EasyStock.Web.Models.ViewModels.Assinatura;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EasyStock.Web.Controllers;

public class AssinaturaController(
    AssinaturaService svc,
    SessionService session,
    IOptions<MarketingOptions> mkt) : BaseController(session)
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

    // Landing de bloqueio (takeover full-screen, Layout=null). Para onde o roteamento
    // manda o tenant com assinatura bloqueada (trial vencido/suspenso/cancelado) em vez
    // do loop de criar loja (#619). NÃO exige lojaId (controller na allowlist do BaseController)
    // e só consome rotas liberadas pelo gate (/api/planos público, /api/assinatura allowlistado).
    [HttpGet("/assinatura/bloqueado")]
    public async Task<IActionResult> Bloqueado()
    {
        // Sub-code vem do roteamento via TempData; acesso direto à URL cai no default.
        var subCode = TempData["AssinaturaBloqueioCode"] as string;
        var vm = AssinaturaBloqueadaViewModel.ParaMotivo(subCode);

        var planosResult = await svc.ListarPlanosAsync();
        if (planosResult.Success && planosResult.Data is { } planos)
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

        vm.WhatsAppNumero = mkt.Value.WhatsAppNumero;
        vm.LinkWhatsApp = mkt.Value.LinkWhatsApp;
        vm.EmailContato = mkt.Value.EmailContato;

        return View(vm);
    }
}
