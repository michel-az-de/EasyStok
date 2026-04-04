using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
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

    [HttpPost]
    public async Task<IActionResult> Create(Venda venda)
    {
        await _vendaRepository.AddAsync(venda);
        return CreatedAtAction(nameof(GetById), new { id = venda.Id }, venda);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, Venda venda)
    {
        if (id != venda.Id) return BadRequest();
        await _vendaRepository.UpdateAsync(venda);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        await _vendaRepository.DeleteAsync(id);
        return NoContent();
    }
}