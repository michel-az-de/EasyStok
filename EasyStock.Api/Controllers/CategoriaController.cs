using EasyStock.Api.Http;
using EasyStock.Application.UseCases.GerenciarCategoria;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Categories / Categorias")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/categorias")]
public class CategoriaController(GerenciarCategoriaUseCase useCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId)
        => DataOk(await useCase.ListarAsync(empresaId));

    [SwaggerOperation(Summary = "Get category details")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var categoria = await useCase.ObterAsync(id, empresaId);
        return categoria is null ? DataNotFound() : DataOk(categoria);
    }

    [SwaggerOperation(Summary = "Create category")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost]
    public async Task<IActionResult> Create(CriarCategoriaCommand command)
    {
        var result = await useCase.CriarAsync(command);
        return DataCreated($"/api/categorias/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Update category")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, AtualizarCategoriaCommand command)
    {
        if (id != command.Id)
            return DataBadRequest("Id da rota nao confere com o corpo da requisicao.");
        return DataOk(await useCase.AtualizarAsync(command));
    }

    public sealed record AtualizarLimiarCategoriaBody(int? QuantidadeMinima, int? QuantidadeCritica);

    [SwaggerOperation(Summary = "Update category stock thresholds (override)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPatch("{id:guid}/limiar")]
    public async Task<IActionResult> UpdateLimiar(Guid id, [FromQuery] Guid empresaId, [FromBody] AtualizarLimiarCategoriaBody body)
    {
        await useCase.AtualizarLimiaresAsync(empresaId, id, body.QuantidadeMinima, body.QuantidadeCritica);
        return NoContent();
    }

    [SwaggerOperation(Summary = "Delete category")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        await useCase.RemoverAsync(id, empresaId);
        return NoContent();
    }
}
