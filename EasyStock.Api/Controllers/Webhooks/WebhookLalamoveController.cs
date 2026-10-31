using System.Text;
using EasyStock.Application.UseCases.Atendimento.Entregas;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers.Webhooks;

/// <summary>
/// Webhook da Lalamove (#2160, ADR-0064 D5). A URL é a mesma para todas as lojas: a corrida é achada pelo id do
/// pedido e a assinatura é conferida com a conexão da loja dela. Responde 200 a tudo que não é falha transitória,
/// porque a Lalamove desativa a URL depois de 10 falhas seguidas; o estado só muda pela reconsulta.
/// </summary>
[ApiController]
[Route("api/webhooks/lalamove")]
[AllowAnonymous]
[IgnoreAntiforgeryToken] // Servidor-a-servidor autenticado pela assinatura da loja, sem cookie: CSRF não se aplica.
public class WebhookLalamoveController(
    ProcessarWebhookLalamoveUseCase processarUseCase,
    ILogger<WebhookLalamoveController> logger) : ControllerBase
{
    [HttpPost]
    [EnableRateLimiting("webhook-meta")]
    public async Task<IActionResult> Receber(CancellationToken ct)
    {
        using var leitor = new StreamReader(Request.Body, Encoding.UTF8);
        var corpo = await leitor.ReadToEndAsync(ct);
        try
        {
            var desfecho = await processarUseCase.ExecuteAsync(corpo, ct);
            logger.LogInformation("Webhook Lalamove: {Desfecho}.", desfecho);
            return Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Falha nossa ou da reconsulta: 5xx faz a Lalamove reenviar, e a consulta de contingência também cobre.
            logger.LogError(ex, "Webhook Lalamove falhou no processamento.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}
