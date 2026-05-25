using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Storefront.Agendamento;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoint público de janelas de entrega disponíveis (TASK-EZ-AGEND-001).
///
/// <para>
/// Retorna a matriz (data × janela) com vagas restantes para os próximos N dias
/// (default 14). CEP opcional filtra por zona de entrega coberta. Concorrência
/// é eventual — o acerto ocorre no checkout com INSERT atômico (ADR-0014).
/// </para>
///
/// <para>
/// <strong>Anônimo</strong>: cliente não autenticado nesta fase. Tenant
/// resolvido via slug na rota. Cache de 60 s na borda.
/// </para>
/// </summary>
[SwaggerTag("Storefront Agendamento / Janelas de entrega disponíveis")]
[ApiController]
[Route("api/storefront/{slug}/janelas")]
[AllowAnonymous]
public sealed class AgendamentoController(
    ListarJanelasDisponiveisUseCase listarJanelasUseCase) : EasyStockControllerBase
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Lista janelas de entrega disponíveis para o storefront.
    /// </summary>
    [SwaggerOperation(
        Summary = "Listar janelas de entrega disponíveis",
        Description = "Retorna matriz (data × janela) com vagasRestantes e flag esgotado. " +
                      "Período padrão: hoje..hoje+14d. Máximo: 60 dias. " +
                      "CEP opcional filtra por zona coberta. Cache de 60s.")]
    [ProducesResponseType(typeof(IReadOnlyList<JanelaDisponivelDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [HttpGet]
    public async Task<IActionResult> ListarJanelas(
        [FromRoute] string slug,
        [FromQuery] DateOnly? dataInicio,
        [FromQuery] DateOnly? dataFim,
        [FromQuery] string? cep,
        CancellationToken ct)
    {
        try
        {
            var result = await listarJanelasUseCase.ExecuteAsync(
                new ListarJanelasDisponiveisInput(slug, dataInicio, dataFim, cep), ct);

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
        catch (RegraDeDominioVioladaException ex)
        {
            SetNoStore();
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Parâmetros inválidos",
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
