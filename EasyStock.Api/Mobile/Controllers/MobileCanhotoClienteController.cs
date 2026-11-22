using EasyStock.Api.Mobile.Security;
using EasyStock.Application.Services.Pedidos;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// #3190: canhoto da Casa para a dona mandar pelo WhatsApp do próprio celular. A empresa vem do pareamento da chave
/// <c>X-Mobile-Api-Key</c>, nunca da URL. Pedido que ainda não sincronizou responde 409: o app pede o sync primeiro.
/// </summary>
[ApiController]
[Route("api/mobile/pedidos/{mobileOrderId}/canhoto-cliente")]
[MobileApiKey]
[AllowAnonymous]
public sealed class MobileCanhotoClienteController(CanhotoClienteDoApp canhoto) : EasyStockControllerBase
{
    private const string SincronizePrimeiro = "Pedido ainda não sincronizado. Sincronize e tente de novo.";

    [HttpGet]
    public async Task<IActionResult> Obter(string mobileOrderId, CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "Aparelho não pareado." });

        var imagem = await canhoto.GerarAsync(device.EmpresaId, mobileOrderId, ct);
        if (imagem is null) return Conflict(new { error = SincronizePrimeiro });

        Response.Headers.CacheControl = "no-store";
        return File(imagem.Conteudo, imagem.ContentType);
    }

    [HttpPost("aberto")]
    public async Task<IActionResult> Aberto(string mobileOrderId, CancellationToken ct)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "Aparelho não pareado." });

        return await canhoto.RegistrarAbertoAsync(device.EmpresaId, mobileOrderId, ct)
            ? NoContent()
            : Conflict(new { error = SincronizePrimeiro });
    }
}
