using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/vendas")]
public class VendaController(IVendaRepository vendaRepository) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (vendas, totalCount) = await vendaRepository.GetVendasPorEmpresaAsync(empresaId, page, pageSize);
        return DataPaged(vendas, totalCount, page, pageSize);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var venda = await vendaRepository.GetByIdAsync(empresaId, id);
        return venda is null ? DataNotFound() : DataOk(venda);
    }
}
