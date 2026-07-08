using System.Text.Json;
using EasyStock.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Web.Controllers;

/// <summary>
/// BFF dos banners de plataforma (#869): repassa para a API real com o token da sessão.
/// O render é client-side (_Banner / _BannerModal). Confirmar/visto são idempotentes e
/// não-destrutivos — dispensam antiforgery (a ação é protegida pela sessão do usuário).
/// </summary>
public class BannersController(ApiClient api, SessionService session) : BaseController(session)
{
    [HttpGet("/api/banners/ativos")]
    public async Task<IActionResult> Ativos()
    {
        Response.Headers.CacheControl = "no-store"; // correção > flicker; servidor é a verdade
        var r = await api.GetAsync<JsonElement>("/api/banners/ativos");
        return Ok(new { data = r.Success ? r.Data : (object)Array.Empty<object>() });
    }

    [HttpPost("/api/banners/{id:guid}/confirmar")]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> Confirmar(Guid id) => RegistrarAsync(id, "confirmar");

    [HttpPost("/api/banners/{id:guid}/visto")]
    [IgnoreAntiforgeryToken]
    public Task<IActionResult> Visto(Guid id) => RegistrarAsync(id, "visto");

    private async Task<IActionResult> RegistrarAsync(Guid id, string acao)
    {
        var r = await api.PostNoBodyAsync($"/api/banners/{id}/{acao}");
        if (r.Success) return NoContent();
        return StatusCode(r.HttpStatus > 0 ? r.HttpStatus : StatusCodes.Status502BadGateway);
    }
}
