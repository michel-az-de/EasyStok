using EasyStock.Application.UseCases.GerenciarFornecedor;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/fornecedores")]
[Authorize(Policy = "Operador")]
public class FornecedorController(GerenciarFornecedorUseCase fornecedorUseCase) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (fornecedores, total) = await fornecedorUseCase.ListarAsync(empresaId, page, pageSize);
        return Ok(new { Fornecedores = fornecedores, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}")]
    public Task<IActionResult> GetById(Guid id)
    {
        // Direct retrieval not in use case; return NotFound as placeholder
        return Task.FromResult<IActionResult>(NotFound());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CriarFornecedorCommand command)
    {
        var resultado = await fornecedorUseCase.CriarAsync(command);
        return Created("", resultado);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarFornecedorCommand command)
    {
        await fornecedorUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        await fornecedorUseCase.DesativarAsync(id, empresaId);
        return NoContent();
    }
}
