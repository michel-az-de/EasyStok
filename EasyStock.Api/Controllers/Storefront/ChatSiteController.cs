using System.Text.Json;
using EasyStock.Api.Configuration;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.UseCases.Common;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Chat do site (S36, ADR-0051): o visitante da vitrine conversa com a loja. Público e anônimo; a
/// sessão é o header <c>X-Chat-Token</c> (fora da URL, para não ir para log de acesso), devolvido uma
/// vez pelo <c>POST sessoes</c>. A resposta do console chega pelo <c>GET stream</c> (SSE), que lê
/// do banco: funciona com várias instâncias da API e não perde mensagem se a conexão cair (o cliente
/// reconecta passando <c>depois</c> com o instante da última que recebeu).
///
/// <para>
/// O widget deve ler o stream com <c>fetch</c> e o header, não com <c>EventSource</c> (que não manda
/// header). O stream fecha sozinho em 5 min; reconectar é o fluxo normal.
/// </para>
/// </summary>
[SwaggerTag("Website chat (public)")]
[ApiController]
[Route("api/public/chat/{slug}")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Sem cookie: a sessão é o header X-Chat-Token, então CSRF não se aplica.
public class ChatSiteController(
    AcessoChatSite acesso,
    AbrirSessaoChatSiteUseCase abrirSessao,
    EnviarMensagemVisitanteUseCase enviarMensagem,
    ListarMensagensChatSiteUseCase listarMensagens) : EasyStockControllerBase
{
    public static readonly TimeSpan DuracaoMaximaStream = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan IntervaloStream = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan IntervaloPing = TimeSpan.FromSeconds(15);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [SwaggerOperation(Summary = "Open an anonymous chat session", Description = "Devolve o token uma vez só. 404 se a loja não tem o chat ligado.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(ChatSiteRateLimit.AbrirSessao)]
    [HttpPost("sessoes")]
    public Task<IActionResult> AbrirSessao(string slug, CancellationToken ct) =>
        Tratar(async () => DataOk(await abrirSessao.ExecuteAsync(slug, ct)));

    [SwaggerOperation(Summary = "Send a visitor message")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting(ChatSiteRateLimit.Mensagem)]
    [HttpPost("mensagens")]
    public Task<IActionResult> EnviarMensagem(
        string slug, [FromHeader(Name = ChatSiteRateLimit.HeaderToken)] string? token,
        [FromBody] MensagemVisitanteBody body, CancellationToken ct) =>
        Tratar(async () => DataOk(await enviarMensagem.ExecuteAsync(slug, token, body?.Texto, ct)));

    [SwaggerOperation(Summary = "List messages after an instant (polling fallback)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [EnableRateLimiting(ChatSiteRateLimit.Leitura)]
    [HttpGet("mensagens")]
    public Task<IActionResult> ListarMensagens(
        string slug, [FromHeader(Name = ChatSiteRateLimit.HeaderToken)] string? token,
        [FromQuery] DateTime? depois, CancellationToken ct) =>
        Tratar(async () => DataOk(await listarMensagens.ExecuteAsync(slug, token, depois, ct)));

    [SwaggerOperation(Summary = "Server-sent events with the store replies",
        Description = "event: mensagem (JSON da mensagem); event: sessao_encerrada quando a sessão vence; comentário ': ping' a cada 15 s.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [EnableRateLimiting(ChatSiteRateLimit.Leitura)]
    [HttpGet("stream")]
    public async Task<IActionResult> Stream(
        string slug, [FromHeader(Name = ChatSiteRateLimit.HeaderToken)] string? token,
        [FromQuery] DateTime? depois, CancellationToken ct)
    {
        Guid empresaId;
        try
        {
            empresaId = (await acesso.ResolverSessaoAsync(slug, token, DateTime.UtcNow, ct)).EmpresaId;
        }
        catch (Exception ex) when (ex is ChatSiteIndisponivelException or SessaoChatSiteInvalidaException)
        {
            return Erro(ex);
        }

        var tokenHash = AcessoChatSite.HashDoToken(token!.Trim());
        Response.StatusCode = StatusCodes.Status200OK;
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        var cursor = depois;
        var fim = DateTime.UtcNow + DuracaoMaximaStream;
        var ultimoEnvio = DateTime.UtcNow;
        try
        {
            while (!ct.IsCancellationRequested && DateTime.UtcNow < fim)
            {
                IReadOnlyList<MensagemChatSiteResult> novas;
                try
                {
                    novas = await listarMensagens.ListarParaStreamAsync(empresaId, tokenHash, cursor, ct);
                }
                catch (SessaoChatSiteInvalidaException)
                {
                    await EscreverAsync("event: sessao_encerrada\ndata: {}\n\n", ct);
                    break;
                }

                foreach (var m in novas)
                {
                    await EscreverAsync($"id: {m.Id}\nevent: mensagem\ndata: {JsonSerializer.Serialize(m, Json)}\n\n", ct);
                    cursor = m.EnviadaEm;
                }

                if (novas.Count > 0) ultimoEnvio = DateTime.UtcNow;
                else if (DateTime.UtcNow - ultimoEnvio >= IntervaloPing)
                {
                    await EscreverAsync(": ping\n\n", ct);
                    ultimoEnvio = DateTime.UtcNow;
                }

                await Task.Delay(IntervaloStream, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // O visitante fechou a aba: fim normal do stream.
        }

        return new EmptyResult();
    }

    private async Task EscreverAsync(string texto, CancellationToken ct)
    {
        await Response.WriteAsync(texto, ct);
        await Response.Body.FlushAsync(ct);
    }

    private async Task<IActionResult> Tratar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (Exception ex) when (ex is ChatSiteIndisponivelException or SessaoChatSiteInvalidaException)
        {
            return Erro(ex);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }

    private IActionResult Erro(Exception ex) => ex is ChatSiteIndisponivelException
        ? DataNotFound(ex.Message)
        : StatusCode(StatusCodes.Status403Forbidden, new ApiErrorResponse(new ApiError("SESSAO_CHAT_INVALIDA", ex.Message, null, null)));
}

public sealed record MensagemVisitanteBody(string? Texto);
