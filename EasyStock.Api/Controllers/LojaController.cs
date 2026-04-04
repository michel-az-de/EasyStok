using EasyStock.Application.UseCases.GerenciarLoja;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/lojas")]
[Authorize(Policy = "Gerente")]
public class LojaController(GerenciarLojaUseCase lojaUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId)
    {
        var lojas = await lojaUseCase.ListarAsync(empresaId);
        return Ok(lojas);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CriarLojaCommand command)
    {
        var resultado = await lojaUseCase.CriarAsync(command);
        return Created("", resultado);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarLojaCommand command)
    {
        await lojaUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        await lojaUseCase.DesativarAsync(id, empresaId);
        return NoContent();
    }
}
