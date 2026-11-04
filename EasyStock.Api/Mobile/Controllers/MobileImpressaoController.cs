using System.Globalization;
using EasyStock.Api.Mobile.Security;
using EasyStock.Application.UseCases.ImpressaoLoja;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Regra de impressão da loja para o aparelho pareado (#2209, F4 da impressão v2).
/// Só leitura: a regra se grava no Console e na Web (<c>PUT api/lojas/{lojaId}/impressao</c>).
/// Empresa e loja vêm do pareamento da chave <c>X-Mobile-Api-Key</c>, nunca da URL, então um
/// aparelho só lê a regra da própria loja.
/// </summary>
[ApiController]
[Route("api/mobile/impressao")]
[MobileApiKey]
[AllowAnonymous]
public sealed class MobileImpressaoController(ObterRegraImpressaoLojaUseCase obterUseCase) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken ct)
    {
        // Sem a chave e com RequireApiKey=false o filtro deixa passar sem aparelho: aqui não há loja.
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "Aparelho não pareado." });

        var regra = await obterUseCase.ExecuteAsync(new ObterRegraImpressaoLojaQuery(device.EmpresaId, device.LojaId), ct);
        if (regra is null) return DataNotFound("Loja não encontrada.");

        Response.Headers.ETag = $"\"{regra.Versao.ToString(CultureInfo.InvariantCulture)}\"";
        return DataOk(regra);
    }
}
