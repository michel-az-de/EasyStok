using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EasyStock.Application.Ports.Output.Atendimento;
using EasyStock.Application.UseCases.Atendimento.Webhook;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/wppconnect")]
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class WebhookWppConnectController(ProcessarWebhookWppConnectUseCase processar, IConfiguration configuration,
    ILogger<WebhookWppConnectController> logger)
    : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("webhook-meta")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Receber([FromQuery] string? token, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Atendimento:WppConnect:Habilitado")) return Unauthorized();
        using var memoria = new MemoryStream();
        await Request.Body.CopyToAsync(memoria, ct);
        var corpo = memoria.ToArray();
        switch (Autenticar(corpo, Request.Headers[HeaderAssinatura].ToString(), token,
                    configuration["Atendimento:WppConnect:WebhookToken"],
                    configuration.GetValue("Atendimento:WppConnect:AceitarTokenNaQuery", true)))
        {
            case Autenticacao.Recusado:
                return Unauthorized();
            case Autenticacao.TokenNaQuery:
                // #3097: transição. Zero ocorrências por 7 dias libera desligar AceitarTokenNaQuery e remover o caminho.
                logger.LogWarning("Callback WPPConnect autenticado pelo token na URL; atualize o compose e o preload da ponte (#3097).");
                break;
        }
        var rawBody = Encoding.UTF8.GetString(corpo);
        try
        {
            return await processar.ExecuteAsync(rawBody, ct) switch
            {
                true => Ok(), false => StatusCode(503), null => NotFound()
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentOutOfRangeException)
        {
            logger.LogWarning("Callback WPPConnect inválido: {Motivo}", ex.Message);
            return BadRequest(new { error = new { message = "Evento WPPConnect inválido." } });
        }
        catch (Exception ex) when (ex is WhatsAppCloudException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Callback WPPConnect indisponível: {TipoErro}, HTTP {StatusHttp}, código {Codigo}",
                ex.GetType().Name, (ex as WhatsAppCloudException)?.StatusHttp, (ex as WhatsAppCloudException)?.Codigo);
            return StatusCode(503);
        }
    }

    /// <summary>Cabeçalho com o HMAC sha512 do corpo, posto pelo preload <c>scripts/wppconnect/configurar.cjs</c> (#3097).</summary>
    public const string HeaderAssinatura = WebhookWahaController.HeaderAssinatura;

    public enum Autenticacao { Recusado, Assinado, TokenNaQuery }

    /// <summary>
    /// Corpo assinado vale sempre; o token na URL só enquanto <paramref name="aceitarTokenNaQuery"/> (transição da ponte já
    /// instalada, #3097). Sem assinatura com timestamp o mesmo corpo pode ser reenviado: a rede é interna e o webhook é idempotente.
    /// </summary>
    public static Autenticacao Autenticar(byte[] corpo, string? assinatura, string? token, string? configurado, bool aceitarTokenNaQuery)
    {
        if (configurado is not { Length: >= 16 }) return Autenticacao.Recusado;
        if (WebhookWahaController.AssinaturaValida(assinatura, corpo, configurado)) return Autenticacao.Assinado;
        return aceitarTokenNaQuery && TokenValido(token, configurado) ? Autenticacao.TokenNaQuery : Autenticacao.Recusado;
    }

    public static bool TokenValido(string? recebido, string? configurado) =>
        configurado is { Length: >= 16 } && recebido is { Length: > 0 and <= 512 }
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(recebido)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configurado)));
}
