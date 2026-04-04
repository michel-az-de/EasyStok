using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/vendas")]
public class VendaController : ControllerBase
{
    private readonly IVendaRepository _vendaRepository;

    public VendaController(IVendaRepository vendaRepository)
    {
        _vendaRepository = vendaRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (vendas, totalCount) = await _vendaRepository.GetVendasPorEmpresaAsync(empresaId, page, pageSize);
        return Ok(new { Items = vendas, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var venda = await _vendaRepository.GetByIdAsync(empresaId, id);
        if (venda == null) return NotFound();
        return Ok(venda);
    }

}
