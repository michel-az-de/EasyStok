using EasyStock.Api.Services.Relatorios;
using EasyStock.Application.UseCases.Reports;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Download do artefato de relatório pelo link assinado (#2668). Anônimo porque o link abre numa aba nova do Web, sem
/// o JWT; quem autoriza é o token, válido por 15 min. Token inválido, vencido, de outra empresa, chave que não bate ou
/// arquivo ausente dão o mesmo 404, sem dizer qual foi.
/// </summary>
[SwaggerTag("Report artifacts (signed link)")]
[ApiController]
[Route(RelatorioDownloadLink.Rota)]
[AllowAnonymous]
[EnableRateLimiting("mobile-anonymous")]
public sealed class ReportArtefatosController(BaixarArtefatoRelatorioUseCase baixar) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Download a report artifact by signed token")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{token}")]
    public async Task<IActionResult> Baixar(string token, CancellationToken ct = default)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers.XContentTypeOptions = "nosniff";

        var artefato = await baixar.ExecuteAsync(token, ct);
        if (artefato is null) return NotFound();

        // FileStreamResult com nome: Content-Disposition attachment; o tipo vem do formato gravado na run.
        return File(artefato.Conteudo, artefato.ContentType, artefato.NomeArquivo);
    }
}
