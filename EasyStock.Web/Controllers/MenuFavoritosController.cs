using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// BFF de escrita dos favoritos do menu (ADR-0032, fatia 4b). Apenas PUT (D3: sem GET
/// publico — o TagHelper renderiza no servidor e o Alpine hidrata do JSON inline).
/// Herda BaseController: XHR sem sessao -> 401, sem loja -> 409 (esFetch trata).
/// Antiforgery via header RequestVerificationToken (enviado pelo JS na fatia 7).
/// </summary>
public class MenuFavoritosController(PreferenciaMenuService svc, SessionService session) : BaseController(session)
{
    [HttpPut("/menu/favoritos")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Salvar([FromBody] SalvarFavoritosBody body)
    {
        var normalizada = await svc.SalvarAsync(
            session.GetUsuarioId(), session.GetLojaId(), body.Favoritos ?? new List<string>());

        return normalizada is null
            ? StatusCode(502, new { ok = false })
            : Json(new { ok = true, favoritos = normalizada });
    }

    public sealed record SalvarFavoritosBody(List<string>? Favoritos);
}
