using EasyStock.Api.Http;
using EasyStock.Application.UseCases.GerenciarCategoria;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/categorias")]
public class CategoriaController(GerenciarCategoriaUseCase useCase) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] Guid empresaId)
        => DataOk(await useCase.ListarAsync(empresaId));

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(Guid id, [FromQuery] Guid empresaId)
    {
        var categoria = await useCase.ObterAsync(id, empresaId);
        return categoria is null ? DataNotFound() : DataOk(categoria);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CriarCategoriaCommand command)
    {
        var result = await useCase.CriarAsync(command);
        return DataCreated($"/api/categorias/{result.Id}", result);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(Guid id, AtualizarCategoriaCommand command)
    {
        if (id != command.Id)
            return DataBadRequest("Id da rota nao confere com o corpo da requisicao.");
        return DataOk(await useCase.AtualizarAsync(command));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] Guid empresaId)
    {
        await useCase.RemoverAsync(id, empresaId);
        return NoContent();
    }
}
