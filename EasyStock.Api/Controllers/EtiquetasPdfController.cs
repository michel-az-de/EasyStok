using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Etiquetas.Folha;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Folha A4 de etiquetas em PDF")]
[ApiController]
[Route("api/etiquetas/pdf")]
[Authorize]
public class EtiquetasPdfController(GerarFolhaEtiquetasPdfUseCase gerarUseCase) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Gera a folha A4 de etiquetas em PDF",
        Description = "Recebe o LayoutJson v1 já resolvido de cada etiqueta e posiciona na grade Pimaco A4256 ou em 'quantas cabem'. " +
                      "Recusa com 400 o que sairia ilegível ou sem zona de silêncio nos códigos.")]
    [HttpPost]
    [Authorize(Policy = "Operador")]
    [Produces("application/pdf", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Gerar([FromBody] GerarFolhaEtiquetasPdfCommand command, CancellationToken ct)
    {
        try
        {
            var pdf = await gerarUseCase.ExecuteAsync(command, ct);
            Response.Headers.CacheControl = "private, no-store";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(pdf, "application/pdf");
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}
