using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Storefront.Avaliacao;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoints de avaliação pós-pedido (TASK-EZ-AVAL-001).
///
/// <para>
/// Fluxo: link WhatsApp aponta para GET /avaliar/abrir?p=pedidoId&amp;t=jwt
/// → valida JWT → seta cookie HttpOnly → 302 /avaliar/{pedidoId} (URL limpa).
/// Frontend usa o cookie para POST /api/storefront/{slug}/avaliacoes.
/// </para>
/// </summary>
[SwaggerTag("Storefront Avaliacao")]
[ApiController]
[AllowAnonymous]
public sealed class AvaliacaoController(
    AbrirPaginaAvaliacaoUseCase abrirUseCase,
    CriarAvaliacaoPedidoUseCase criarUseCase,
    ListarAvaliacoesPublicoUseCase listarUseCase) : EasyStockControllerBase
{
    private const int CookieMaxAge = 60 * 60 * 24 * 30; // 30 dias em segundos

    // ── GET /avaliar/abrir ────────────────────────────────────────────────────

    [SwaggerOperation(
        Summary = "Abre página de avaliação via link WhatsApp",
        Description = "Valida JWT, seta cookie HttpOnly __Host-cdb_aval_{pedidoId} e redireciona para /avaliar/{pedidoId}.")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [HttpGet("/avaliar/abrir")]
    public async Task<IActionResult> AbrirPaginaAvaliacao(
        [FromQuery(Name = "p")] Guid pedidoId,
        [FromQuery(Name = "t")] string token)
    {
        try
        {
            var result = await abrirUseCase.ExecuteAsync(new AbrirPaginaAvaliacaoInput(pedidoId, token));

            var cookieName = $"__Host-cdb_aval_{result.PedidoId}";
            Response.Cookies.Append(cookieName, result.CookieValue, new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
                Path = "/avaliar",
                MaxAge = TimeSpan.FromSeconds(CookieMaxAge),
            });

            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true };
            return Redirect($"/avaliar/{result.PedidoId}");
        }
        catch (AvaliacaoTokenInvalidoException ex)
        {
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
            {
                Status = StatusCodes.Status410Gone,
                Title = "Link expirou",
                Detail = ex.Message,
            });
        }
    }

    // ── POST /api/storefront/{slug}/avaliacoes ────────────────────────────────

    [SwaggerOperation(
        Summary = "Submete avaliação do pedido",
        Description = "Requer cookie __Host-cdb_aval_{pedidoId} emitido pelo GET /avaliar/abrir.")]
    [ProducesResponseType(typeof(AvaliacaoCriadaDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [HttpPost("/api/storefront/{slug}/avaliacoes")]
    public async Task<IActionResult> CriarAvaliacao(
        [FromRoute] string slug,
        [FromBody] CriarAvaliacaoRequestBody body,
        CancellationToken ct)
    {
        var cookieName = $"__Host-cdb_aval_{body.PedidoId}";
        if (!Request.Cookies.TryGetValue(cookieName, out var cookieValue)
            || string.IsNullOrWhiteSpace(cookieValue))
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Autorização necessária",
                Detail = "Cookie de avaliação ausente. Abra o link enviado pelo WhatsApp.",
            });
        }

        var input = new CriarAvaliacaoPedidoInput(
            Slug: slug,
            PedidoId: body.PedidoId,
            Nota: body.Nota,
            Comentario: body.Comentario,
            RecomendariaParaAmigos: body.RecomendariaParaAmigos,
            FotoUrl: body.FotoUrl,
            CookieValue: cookieValue);

        try
        {
            var result = await criarUseCase.ExecuteAsync(input, ct);
            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true };
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (AvaliacaoCookieAusenteException ex)
        {
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Cookie inválido",
                Detail = ex.Message,
            });
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (PedidoNaoElegivelParaAvaliacaoException ex)
        {
            return StatusCode(StatusCodes.Status422UnprocessableEntity, new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Pedido não elegível para avaliação",
                Detail = ex.Message,
            });
        }
        catch (AvaliacaoDuplicadaException ex)
        {
            return StatusCode(StatusCodes.Status409Conflict, new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Pedido já avaliado",
                Detail = $"Este pedido já possui uma avaliação (id={ex.AvaliacaoId}).",
            });
        }
        catch (EasyStock.Domain.Exceptions.RegraDeDominioVioladaException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    // ── GET /api/storefront/{slug}/avaliacoes ─────────────────────────────────

    [SwaggerOperation(
        Summary = "Lista avaliações públicas do storefront",
        Description = "Retorna avaliações visíveis com nota, comentário e primeiro nome do cliente. Sem PII.")]
    [ProducesResponseType(typeof(ListarAvaliacoesResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("/api/storefront/{slug}/avaliacoes")]
    public async Task<IActionResult> ListarAvaliacoes(
        [FromRoute] string slug,
        [FromQuery] int? page,
        [FromQuery] int? limit,
        CancellationToken ct)
    {
        try
        {
            var result = await listarUseCase.ExecuteAsync(
                new ListarAvaliacoesInput(slug, page, limit), ct);

            return DataOk(result);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
    }
}

/// <summary>Body do POST /avaliacoes.</summary>
public sealed record CriarAvaliacaoRequestBody(
    Guid PedidoId,
    int Nota,
    string? Comentario,
    bool RecomendariaParaAmigos,
    string? FotoUrl = null);
