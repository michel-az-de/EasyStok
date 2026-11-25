using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Pedidos;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// #3294: os indicadores da tela Pedidos de Cadastros. Mesmos filtros e mesma permissão da lista
/// (<c>GET api/pedidos</c>), menos situação, página e ordem. A rota literal vence o <c>api/pedidos/{id}</c>.
/// </summary>
[SwaggerTag("Pedidos (encomendas)")]
[ApiController]
[Route("api/pedidos/resumo")]
[Authorize]
[ValidateEmpresaId]
public sealed class PedidosResumoController(ResumoPedidosUseCase resumo, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Order indicators for the filtered list (sales, receivables, actions, late, cancelled, customers)")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? empresaId,
        [FromQuery] Guid? clienteId = null,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? ate = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? entregaDesde = null,
        [FromQuery] DateTime? entregaAte = null,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var result = await resumo.ExecuteAsync(new ResumoPedidosQuery(
            emp, clienteId, DataUtc.InicioDoPeriodoUtc(desde), DataUtc.FimDoPeriodoUtc(ate), search,
            DataUtc.InicioDoPeriodoUtc(entregaDesde), DataUtc.FimDoPeriodoUtc(entregaAte)), ct);
        return DataOk(result);
    }
}
