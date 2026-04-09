using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Sales / Vendas")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/vendas")]
public class VendaController(IVendaRepository vendaRepository) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List sales (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (vendas, totalCount) = await vendaRepository.GetVendasPorEmpresaAsync(empresaId, page, pageSize);
        return DataPaged(vendas, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get sale details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var venda = await vendaRepository.GetByIdAsync(empresaId, id);
        return venda is null ? DataNotFound() : DataOk(venda);
    }

    [SwaggerOperation(Summary = "Get sale items", Description = "Returns the list of items that compose a sale.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}/itens")]
    public async Task<IActionResult> GetItens(Guid id, [FromQuery] Guid empresaId)
    {
        var venda = await vendaRepository.GetByIdAsync(empresaId, id);
        if (venda is null)
            return DataNotFound();

        var itens = venda.ItensVenda ?? Enumerable.Empty<Domain.Entities.ItemVenda>();
        return DataOk(itens);
    }
}
