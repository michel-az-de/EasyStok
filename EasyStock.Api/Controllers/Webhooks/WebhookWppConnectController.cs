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
public sealed class WebhookWppConnectController(ProcessarWebhookWppConnectUseCase processar, IConfiguration configuration)
    : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("webhook-meta")]
    [RequestSizeLimit(2_000_000)]
    public async Task<IActionResult> Receber([FromQuery] string? token, CancellationToken ct)
    {
        if (!configuration.GetValue<bool>("Atendimento:WppConnect:Habilitado")
            || !TokenValido(token, configuration["Atendimento:WppConnect:WebhookToken"])) return Unauthorized();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(ct);
        try
        {
            return await processar.ExecuteAsync(rawBody, ct) switch
            {
                true => Ok(), false => StatusCode(503), null => NotFound()
            };
        }
        catch (Exception ex) when (ex is JsonException or FormatException or ArgumentOutOfRangeException)
        { return BadRequest(new { error = new { message = "Evento WPPConnect inválido." } }); }
        catch (Exception ex) when (ex is WhatsAppCloudException or HttpRequestException or TaskCanceledException)
        { return StatusCode(503); }
    }

    public static bool TokenValido(string? recebido, string? configurado) =>
        configurado is { Length: >= 16 } && recebido is { Length: > 0 and <= 512 }
        && CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(recebido)),
            SHA256.HashData(Encoding.UTF8.GetBytes(configurado)));
}
