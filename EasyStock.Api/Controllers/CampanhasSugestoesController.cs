using EasyStock.Application.UseCases.Campanhas.Interesse;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Sugestões para a dona montar campanha ou avisar na mão (S31). Só lista; não envia nada (D8).
/// </summary>
[SwaggerTag("Campaign suggestions")]
[ApiController]
[Route("api/campanhas/sugestoes")]
[Authorize(Policy = "Admin")]
public class CampanhasSugestoesController(
    ListarSugestoesInteresseUseCase listarInteresseUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Customers with open interest in a menu item (Admin only)",
        Description = "Só interesses abertos (AtendidoEm nulo), do mais recente para o mais antigo.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpGet("interesse")]
    public async Task<IActionResult> Interesse([FromQuery] Guid cardapioItemId, CancellationToken ct)
    {
        if (cardapioItemId == Guid.Empty)
            return DataBadRequest("Informe cardapioItemId.");
        return DataOk(await listarInteresseUseCase.ExecuteAsync(currentUser.EmpresaId, cardapioItemId, ct));
    }
}
