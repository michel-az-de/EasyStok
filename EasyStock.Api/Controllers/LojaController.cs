using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output;
using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ListarLojas;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/lojas")]
[Authorize(Policy = "Gerente")]
public class LojaController(
    CriarLojaUseCase criarUseCase,
    AtualizarLojaUseCase atualizarUseCase,
    DesativarLojaUseCase desativarUseCase,
    ListarLojasUseCase listarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        var lojas = await listarUseCase.ExecuteAsync(new ListarLojasQuery(empresaId));
        return DataOk(lojas);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CriarLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await criarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/lojas/{resultado.Id}", resultado);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] AtualizarLojaCommand command)
    {
        if (id != command.LojaId)
            return DataBadRequest("Id da rota nao corresponde ao Id do comando.");

        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        await atualizarUseCase.ExecuteAsync(command);
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await desativarUseCase.ExecuteAsync(new DesativarLojaCommand(id, empresaId));
        return NoContent();
    }
}
