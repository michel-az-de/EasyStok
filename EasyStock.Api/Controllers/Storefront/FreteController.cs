using EasyStock.Application.UseCases.Storefront.Frete;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoint publico de calculo de frete por CEP (TASK-EZ-FRETE-001).
///
/// <para>
/// Cliente envia o CEP (com ou sem mascara), backend resolve a zona ativa do
/// storefront e devolve valor + ETA. Sem cobertura → 422. CEP invalido → 400.
/// Storefront inexistente → 404. Cache HTTP de 24h para CEPs cobertos.
/// </para>
///
/// <para>
/// <strong>Anonimo</strong>: cliente nao esta autenticado nesta fase. Tenant
/// resolvido via slug na rota.
/// </para>
/// </summary>
[SwaggerTag("Storefront Frete / Calculo de frete por CEP")]
[ApiController]
[Route("api/storefront/{slug}/frete")]
[AllowAnonymous]
public sealed class FreteController(
    CalcularFreteUseCase calcularFreteUseCase) : EasyStockControllerBase
{
    /// <summary>TTL de cache HTTP para respostas 200 — 24h (CEPs raramente trocam de zona).</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);

    /// <summary>
    /// Calcula o valor e a estimativa de entrega do frete para um CEP.
    /// </summary>
    [SwaggerOperation(
        Summary = "Calcular frete por CEP",
        Description = "Retorna valor (centavos) + ETA textual quando o CEP esta coberto. 422 quando nao entregamos no CEP. Cache de 24h em 200.")]
    [ProducesResponseType(typeof(FreteCalculadoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [HttpGet]
    public async Task<IActionResult> CalcularFrete(
        [FromRoute] string slug,
        [FromQuery] string? cep,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cep))
        {
            SetNoStore();
            return DataBadRequest("CEP é obrigatório.");
        }

        try
        {
            var result = await calcularFreteUseCase.ExecuteAsync(
                new CalcularFreteInput(slug, cep), ct);

            SetCacheable();
            return Ok(result);
        }
        catch (CepInvalidoException ex)
        {
            SetNoStore();
            return DataBadRequest(ex.Message);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            SetNoStore();
            return DataNotFound(ex.Message);
        }
        catch (CepSemCoberturaException ex)
        {
            SetNoStore();
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "CEP sem cobertura",
                    Detail = ex.Message,
                });
        }
    }

    private void SetCacheable()
    {
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            Public = true,
            MaxAge = CacheTtl,
        };
    }

    private void SetNoStore()
    {
        Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
        {
            NoStore = true,
            NoCache = true,
        };
    }
}
