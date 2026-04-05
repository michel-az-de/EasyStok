using EasyStock.Application.Ports.Output;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.AtualizarFornecedor;
using EasyStock.Application.UseCases.CriarFornecedor;
using EasyStock.Application.UseCases.DesativarFornecedor;
using EasyStock.Application.UseCases.Fornecedor;
using EasyStock.Application.UseCases.ListarFornecedores;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/fornecedores")]
[Authorize]
public class FornecedorController(
    CriarFornecedorUseCase criarUseCase,
    AtualizarFornecedorUseCase atualizarUseCase,
    DesativarFornecedorUseCase desativarUseCase,
    ListarFornecedoresUseCase listarUseCase,
    ObterFornecedorDetalheUseCase obterDetalheUseCase,
    ObterHistoricoFornecedorUseCase obterHistoricoUseCase,
    ObterEstatisticasFornecedorUseCase obterEstatisticasUseCase,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] bool? ativo = null, [FromQuery] string? search = null)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var (fornecedores, total) = await listarUseCase.ExecuteAsync(new ListarFornecedoresQuery(empresaId, page, pageSize, ativo, search));
        return Ok(new { Fornecedores = fornecedores, TotalCount = total, Page = page, PageSize = pageSize });
    }

    [HttpGet("{id}")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && currentUser.EmpresaId != Guid.Empty
            && currentUser.EmpresaId != empresaId)
            return Forbid();

        var fornecedor = await obterDetalheUseCase.ExecuteAsync(new ObterFornecedorDetalheQuery(empresaId, id));
        return Ok(fornecedor);
    }

    [HttpGet("{id}/historico")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetHistorico(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && currentUser.EmpresaId != Guid.Empty
            && currentUser.EmpresaId != empresaId)
            return Forbid();

        var historico = await obterHistoricoUseCase.ExecuteAsync(new ObterHistoricoFornecedorQuery(empresaId, id));
        return Ok(historico);
    }

    [HttpGet("{id}/estatisticas")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> GetEstatisticas(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin
            && currentUser.EmpresaId != Guid.Empty
            && currentUser.EmpresaId != empresaId)
            return Forbid();

        var estatisticas = await obterEstatisticasUseCase.ExecuteAsync(new ObterEstatisticasFornecedorQuery(empresaId, id));
        return Ok(estatisticas);
    }

    [HttpPost]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Create([FromBody] CriarFornecedorCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await criarUseCase.ExecuteAsync(command);
        return Created($"/api/fornecedores/{resultado.Id}", resultado);
    }

    [HttpPatch("{id}")]
    [HttpPut("{id}")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarFornecedorCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();
        if (id != command.FornecedorId)
            return BadRequest(new { error = "FornecedorId da rota difere do corpo." });

        await atualizarUseCase.ExecuteAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> Desativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await desativarUseCase.ExecuteAsync(new DesativarFornecedorCommand(id, empresaId));
        return NoContent();
    }
}
