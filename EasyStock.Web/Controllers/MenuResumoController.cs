using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// BFF dos badges do menu lateral (ADR-0032, fatia 2). <c>GET /menu/resumo</c> devolve
/// JSON cru consumido por <c>menu-badges.js</c>. Herda <see cref="BaseController"/>:
/// XHR sem sessão recebe 401 JSON (não o HTML de login) e sem loja recebe 409 NO_STORE —
/// o <c>esFetch</c> trata ambos. Cache e agregação vivem em <see cref="MenuResumoService"/>.
/// </summary>
public class MenuResumoController(MenuResumoService svc, SessionService session) : BaseController(session)
{
    [HttpGet("/menu/resumo")]
    public async Task<IActionResult> Resumo()
    {
        var (badges, ok) = await svc.ObterAsync(session.GetEmpresaId(), session.GetLojaId());
        return Json(new
        {
            criticos = badges.ProdutosCriticos,
            vencidos = badges.LotesVencidos,
            pedidos = badges.PedidosAbertos,
            dashboard = badges.DashboardTotal,
            ok,
        });
    }
}
