using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Stock Movements / Movimentações")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/movimentacoes")]
public class MovimentacaoController(
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    EasyStock.Application.Ports.Output.ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List stock movements (paginated)", Description = "Filter by date range, movement type (ENTRADA/SAIDA) and nature.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] TipoMovimentacaoEstoque? tipo,
        [FromQuery] NaturezaMovimentacaoEstoque? natureza,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await movimentacaoRepository.GetByEmpresaAsync(
            resolvedEmpresaId, de, ate, tipo, natureza, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get KPI aggregates for movements", Description = "Returns server-side computed KPIs (total units, revenue, sales count, loss count).")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("kpis")]
    public async Task<IActionResult> GetKpis(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] TipoMovimentacaoEstoque? tipo,
        [FromQuery] NaturezaMovimentacaoEstoque? natureza)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var kpis = await movimentacaoRepository.GetKpisAsync(resolvedEmpresaId, de, ate, tipo, natureza);
        return DataOk(kpis);
    }

    [SwaggerOperation(Summary = "List movements for a specific stock item")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("item/{itemEstoqueId}")]
    public async Task<IActionResult> GetByItem(Guid itemEstoqueId)
        => DataOk(await movimentacaoRepository.GetByItemEstoqueAsync(itemEstoqueId));
}
