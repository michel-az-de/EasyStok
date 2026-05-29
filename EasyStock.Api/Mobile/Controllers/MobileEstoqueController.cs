using EasyStock.Api.Mobile.Security;

namespace EasyStock.Api.Mobile.Controllers;

/// <summary>
/// Busca de itens de estoque autenticada por device api key.
/// Suporta o fluxo de PDV mobile: digitar termo, escolher item, vender.
/// </summary>
[ApiController]
[Route("api/mobile/estoque")]
public class MobileEstoqueController(
    IItemEstoqueRepository itemEstoqueRepo) : ControllerBase
{
    [HttpGet("buscar")]
    [MobileApiKey]
    public async Task<IActionResult> Buscar(
        [FromQuery] string q,
        [FromQuery] int limit = 10,
        CancellationToken ct = default)
    {
        var device = HttpContext.GetMobileDevice();
        if (device is null) return Unauthorized(new { error = "device não pareado" });

        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(Array.Empty<object>());

        var clamped = Math.Clamp(limit, 1, 30);
        var items = await itemEstoqueRepo.SearchAsync(device.EmpresaId, q.Trim(), clamped);

        var resultado = items.Select(i => new
        {
            id = i.Id,
            descricao = i.DescricaoAnuncio ?? i.VariacaoDescricao ?? string.Empty,
            cor = i.Cor,
            tamanho = i.Tamanho,
            quantidadeAtual = i.QuantidadeAtual.Value,
            custoUnitario = (decimal)i.CustoUnitario,
            precoVendaSugerido = i.PrecoVendaSugerido != null ? (decimal?)i.PrecoVendaSugerido : null,
            chavePesquisa = i.ChavePesquisa,
            codigoInterno = i.CodigoInterno,
            status = i.Status.ToString()
        });

        return Ok(resultado);
    }
}
