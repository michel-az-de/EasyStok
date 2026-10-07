using EasyStock.Api.Configuration;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Foto da loja no chat do site, para o visitante (#1448). Mesma sessão do <see cref="ChatSiteController"/>
/// (header <c>X-Chat-Token</c>); o widget busca com <c>fetch</c> e mostra o blob. Fica em controller
/// próprio para não disputar o arquivo do chat com outras frentes.
/// </summary>
[SwaggerTag("Website chat (public)")]
[ApiController]
[Route("api/public/chat/{slug}")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Sem cookie: a sessão é o header X-Chat-Token, então CSRF não se aplica.
public sealed class ChatSiteMidiaController(ObterMidiaChatSiteUseCase obterMidia) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Media of a store message in the visitor's chat",
        Description = "404 se a mensagem não é da conversa da sessão, não tem arquivo ou é nota interna; 403 sessão inválida.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EnableRateLimiting(ChatSiteRateLimit.Leitura)]
    [HttpGet("mensagens/{mensagemId:guid}/midia")]
    public async Task<IActionResult> Midia(
        string slug, Guid mensagemId, [FromHeader(Name = ChatSiteRateLimit.HeaderToken)] string? token,
        CancellationToken ct = default)
    {
        try
        {
            var midia = await obterMidia.ExecuteAsync(slug, token, mensagemId, ct);
            if (midia is null) return DataNotFound("Mídia não encontrada.");
            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { Private = true, NoStore = true };
            return File(midia.Conteudo, midia.Mime);
        }
        catch (ChatSiteIndisponivelException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (SessaoChatSiteInvalidaException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden,
                new ApiErrorResponse(new ApiError("SESSAO_CHAT_INVALIDA", ex.Message, null, null)));
        }
    }
}
