using EasyStock.Application.UseCases.ListarPlanos;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Plans / Planos")]
[ApiController]
[Route("api/planos")]
public class PlanoController(ListarPlanosUseCase listarPlanosUseCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List available subscription plans", Description = "Public endpoint — no authentication required.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> GetAll()
        => DataOk(await listarPlanosUseCase.ExecuteAsync());
}
