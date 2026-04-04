using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.GerenciarFornecedor;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/fornecedores")]
[Authorize(Policy = "Operador")]
public class FornecedorController(
    GerenciarFornecedorUseCase fornecedorUseCase,
    IFornecedorRepository fornecedorRepository,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var (fornecedores, total) = await fornecedorUseCase.ListarAsync(empresaId, page, pageSize);
        return Ok(new { Fornecedores = fornecedores, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id)
    {
        var fornecedor = await fornecedorRepository.GetByIdAsync(id);
        return fornecedor is null ? NotFound() : Ok(fornecedor);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CriarFornecedorCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await fornecedorUseCase.CriarAsync(command);
        return Created($"/api/fornecedores/{resultado.Id}", resultado);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarFornecedorCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        await fornecedorUseCase.AtualizarAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await fornecedorUseCase.DesativarAsync(id, empresaId);
        return NoContent();
    }
}
