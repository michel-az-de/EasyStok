using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Infra.Notifications.Options;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("WhatsApp integration status")]
[ApiController]
[Route("api/integracoes/whatsapp")]
[Authorize(Policy = "Admin")]
public class IntegracoesWhatsAppController(
    ObterStatusIntegracaoWhatsAppUseCase obterStatusUseCase,
    IOptions<MetaCloudWhatsAppOptions> metaOptions,
    IConfiguration configuration,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "WhatsApp integration status for the tenant (Admin only)",
        Description = "404 when the ModuloAtendimento feature flag is not active for the tenant.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var status = await obterStatusUseCase.ExecuteAsync(
            new ObterStatusIntegracaoWhatsAppQuery(currentUser.EmpresaId), ct);

        if (status is null)
            return DataNotFound("Modulo de atendimento nao esta ativo para esta empresa.");

        return DataOk(new
        {
            phoneNumberId = status.PhoneNumberId,
            apiVersion = metaOptions.Value.ApiVersion,
            webhookVerificadoEm = status.WebhookVerificadoEm,
            ultimaMensagemRecebidaEm = status.UltimaMensagemRecebidaEm,
            provider = configuration["Notifications:WhatsApp:Provider"] ?? "stub"
        });
    }
}
