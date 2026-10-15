using EasyStock.Application.UseCases.Atendimento;
using EasyStock.Application.UseCases.Common;
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

    /// <summary>
    /// O que o console precisa para abrir o Embedded Signup v4 (#1417). Só valores públicos; <c>habilitado</c> exige
    /// também o segredo do app, sem o qual a troca do <c>code</c> falharia no fim do fluxo, e a KEK corrente, sem a
    /// qual o token da loja não é gravado depois que a Meta já conectou o número.
    /// </summary>
    [SwaggerOperation(Summary = "Public Embedded Signup config for the WhatsApp coexistence flow (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("coexistencia/config")]
    public IActionResult GetConfigCoexistencia()
    {
        var meta = metaOptions.Value;
        var kekCorrente = configuration["Crypto:CurrentKekId"];
        var habilitado = !string.IsNullOrWhiteSpace(meta.AppId)
                         && !string.IsNullOrWhiteSpace(meta.EmbeddedSignupConfigId)
                         && !string.IsNullOrWhiteSpace(meta.AppSecret)
                         && !string.IsNullOrWhiteSpace(kekCorrente)
                         && !string.IsNullOrWhiteSpace(configuration[$"Crypto:Keks:{kekCorrente}"]);
        return DataOk(new
        {
            appId = meta.AppId.Trim(),
            configId = meta.EmbeddedSignupConfigId.Trim(),
            graphVersion = meta.ApiVersion,
            habilitado
        });
    }

    /// <summary>
    /// Conecta o número da loja por coexistência (#1417). Code recusado pela Meta é 400 (o fluxo precisa ser refeito);
    /// recusa da Meta nos passos seguintes é 502; número de outra empresa ou da plataforma é 409.
    /// </summary>
    [SwaggerOperation(Summary = "Connect the store WhatsApp Business number by coexistence (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [HttpPost("coexistencia")]
    public async Task<IActionResult> PostCoexistencia(
        [FromBody] ConectarWhatsAppCoexistenciaRequest req,
        [FromServices] ConectarWhatsAppCoexistenciaUseCase conectarUseCase,
        CancellationToken ct)
    {
        ConexaoWhatsAppResultado r;
        try
        {
            r = await conectarUseCase.ExecuteAsync(new ConectarWhatsAppCoexistenciaCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, req?.Code ?? "", req?.WabaId ?? "", req?.PhoneNumberId ?? ""), ct);
        }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (ConexaoWhatsAppRecusadaException ex) when (ex.Etapa == EtapaConexaoWhatsApp.TrocaDoCode && !ex.MetaIndisponivel)
        {
            return DataBadRequest(ex.Message);
        }
        catch (ConexaoWhatsAppRecusadaException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway,
                new ApiErrorResponse(new ApiError(ex.MetaIndisponivel ? "META_INDISPONIVEL" : "META_RECUSOU",
                    ex.Message, ex.Etapa.ToString(), null)));
        }

        switch (r.Status)
        {
            case StatusConexaoWhatsApp.EmpresaNaoEncontrada:
                return DataNotFound("Empresa não encontrada.");
            case StatusConexaoWhatsApp.NumeroEmUsoPorOutraEmpresa:
                return DataConflict("Este número do WhatsApp já está vinculado a outra empresa.");
            case StatusConexaoWhatsApp.NumeroReservadoDaPlataforma:
                return DataConflict("Este número é o de plataforma do EasyStok e não pode ser vinculado a uma empresa.");
        }

        return DataOk(new
        {
            phoneNumberId = req!.PhoneNumberId.Trim(),
            displayPhoneNumber = r.DisplayPhoneNumber,
            verifiedName = r.VerifiedName,
            isOnBizApp = r.IsOnBizApp,
            platformType = r.PlatformType,
            foraDoAppBusiness = r.ForaDoAppBusiness,
            sincronizacaoEstadoApp = r.SincronizacaoEstadoApp
        });
    }
}

/// <summary>Corpo do <c>POST api/integracoes/whatsapp/coexistencia</c> (#1417).</summary>
public sealed record ConectarWhatsAppCoexistenciaRequest(string Code, string WabaId, string PhoneNumberId);
