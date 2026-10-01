using EasyStock.Api.Authorization;
using EasyStock.Api.Services.Impressao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Impresso de expedição do pedido (S49): etiqueta térmica 10×15, cupom térmico 58 mm ou A4 jato de tinta, em
/// HTML com <c>@page</c> do tamanho do papel. Mesma credencial da fila de impressão (operador do console ou
/// bridge). O canhoto de produção da S20 continua em <c>api/pedidos/{id}/canhoto</c>.
/// </summary>
[SwaggerTag("Operação: impressão")]
[ApiController]
public sealed class PedidoImpressoController(
    MontarPedidoImpressoUseCase montar,
    PedidoImpressoHtml html,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Impresso do pedido (modelo=etiqueta-10x15|cupom-58|a4)")]
    [HttpGet("api/pedidos/{id:guid}/impresso")]
    [Authorize(Policy = ImpressaoApiKeyAuthHandler.PolicyFila)]
    public async Task<IActionResult> Impresso(
        Guid id, [FromQuery] string? modelo, [FromQuery] string? nota, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var m = string.IsNullOrWhiteSpace(modelo) ? PedidoImpressoHtml.Etiqueta10x15 : modelo.Trim().ToLowerInvariant();
        if (!PedidoImpressoHtml.Modelos.Contains(m))
            return DataBadRequest($"modelo deve ser {string.Join(", ", PedidoImpressoHtml.Modelos)}.");

        var pedido = await montar.ExecuteAsync(new MontarPedidoImpressoInput(emp, id, nota), ct);
        if (pedido is null) return DataNotFound("Pedido não encontrado.");

        var baseUrl = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        return Content(await html.RenderizarAsync(pedido, m, baseUrl, ct), "text/html; charset=utf-8");
    }
}
