using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

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
    public async Task<IActionResult> GetAll()
    {
        var vendas = await _vendaRepository.GetAllAsync();
        return Ok(vendas);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var venda = await _vendaRepository.GetByIdAsync(id);
        if (venda == null) return NotFound();
        return Ok(venda);
    }

}
