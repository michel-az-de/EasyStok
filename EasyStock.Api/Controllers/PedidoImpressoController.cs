using EasyStock.Api.Authorization;
using EasyStock.Api.Services.Impressao;
using EasyStock.Application.UseCases.Operacao.Impressao;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Impressos do pedido em HTML com <c>@page</c> do tamanho do papel: o de expedição (S49: etiqueta térmica 10×15,
/// cupom térmico 58 mm ou A4 jato de tinta) e a comanda de cozinha (S52: 10×15 ou 58 mm). Mesma credencial da
/// fila de impressão (operador do console ou bridge). O canhoto da S20 continua em <c>api/pedidos/{id}/canhoto</c>.
/// </summary>
[SwaggerTag("Operação: impressão")]
[ApiController]
public sealed class PedidoImpressoController(
    MontarPedidoImpressoUseCase montar,
    PedidoImpressoHtml html,
    MontarComandaUseCase montarComanda,
    ComandaHtml comandaHtml,
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

        return Content(await html.RenderizarAsync(pedido, m, BaseUrl(), ct), "text/html; charset=utf-8");
    }

    [SwaggerOperation(Summary = "Comanda de cozinha do pedido (modelo=etiqueta-10x15|cupom-58)")]
    [HttpGet("api/pedidos/{id:guid}/comanda")]
    [Authorize(Policy = ImpressaoApiKeyAuthHandler.PolicyFila)]
    public async Task<IActionResult> Comanda(Guid id, [FromQuery] string? modelo, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var m = string.IsNullOrWhiteSpace(modelo) ? ImpressoHtml.Etiqueta10x15 : modelo.Trim().ToLowerInvariant();
        if (!ComandaHtml.Modelos.Contains(m))
            return DataBadRequest($"modelo deve ser {string.Join(", ", ComandaHtml.Modelos)}.");

        var comanda = await montarComanda.ExecuteAsync(new MontarComandaInput(emp, id), ct);
        if (comanda is null) return DataNotFound("Pedido não encontrado.");

        return Content(await comandaHtml.RenderizarAsync(comanda, m, BaseUrl(), ct), "text/html; charset=utf-8");
    }

    private string BaseUrl() => $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
}
