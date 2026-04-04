using EasyStock.Application.UseCases.GerenciarCategoria;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/categorias")]
public class CategoriaController : ControllerBase
{
    private readonly GerenciarCategoriaUseCase _useCase;

    public CategoriaController(GerenciarCategoriaUseCase useCase)
    {
        _useCase = useCase;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId)
    {
        var categorias = await _useCase.ListarAsync(empresaId);
        return Ok(categorias);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var categoria = await _useCase.ObterAsync(id, empresaId);
        if (categoria == null) return NotFound();
        return Ok(categoria);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CriarCategoriaCommand command)
    {
        var result = await _useCase.CriarAsync(command);
        return CreatedAtAction(nameof(GetById), new { id = result.Id, empresaId = result.EmpresaId }, result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, AtualizarCategoriaCommand command)
    {
        if (id != command.Id) return BadRequest("Id da rota nao confere com o corpo da requisicao.");
        var result = await _useCase.AtualizarAsync(command);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        await _useCase.RemoverAsync(id, empresaId);
        return NoContent();
    }
}
