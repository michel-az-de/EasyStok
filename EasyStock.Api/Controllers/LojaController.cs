using EasyStock.Application.UseCases.AtualizarLoja;
using EasyStock.Application.UseCases.CriarLoja;
using EasyStock.Application.UseCases.DesativarLoja;
using EasyStock.Application.UseCases.ReativarLoja;
using EasyStock.Application.UseCases.ListarLojas;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Stores / Lojas")]
[ApiController]
[Route("api/lojas")]
[Authorize]
public class LojaController(
    CriarLojaUseCase criarUseCase,
    AtualizarLojaUseCase atualizarUseCase,
    DesativarLojaUseCase desativarUseCase,
    ReativarLojaUseCase reativarUseCase,
    ListarLojasUseCase listarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List stores (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var lojas = await listarUseCase.ExecuteAsync(new ListarLojasQuery(resolvedEmpresaId));
        return DataOk(lojas);
    }

    [SwaggerOperation(Summary = "Create store (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize(Policy = "Gerente")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CriarLojaCommand command)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != command.EmpresaId)
            return Forbid();

        var resultado = await criarUseCase.ExecuteAsync(command);
        return DataCreated($"/api/lojas/{resultado.Id}", resultado);
    }

    [SwaggerOperation(Summary = "Update store (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
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

    [SwaggerOperation(Summary = "Delete store (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await desativarUseCase.ExecuteAsync(new DesativarLojaCommand(id, empresaId));
        return NoContent();
    }

    [SwaggerOperation(Summary = "Reactivate store (Gerente only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [HttpPost("{id}/reativar")]
    public async Task<IActionResult> Reativar(Guid id, [FromQuery] Guid empresaId)
    {
        if (currentUser.Nivel != NivelAcesso.SuperAdmin && currentUser.EmpresaId != Guid.Empty && currentUser.EmpresaId != empresaId)
            return Forbid();

        await reativarUseCase.ExecuteAsync(new ReativarLojaCommand(id, empresaId));
        return DataOk(new { reativado = true });
    }
}
