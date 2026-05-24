using System.ComponentModel.DataAnnotations;
using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Storefront.Auth;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoints publicos de autenticacao do storefront (ADR-0012).
///
/// <para>
/// <strong>Fluxo</strong>: cliente fornece telefone → POST /solicitar-otp → recebe
/// codigo via WhatsApp/SMS → POST /validar-otp (TASK-EZ-AUTH-002) → recebe cookie
/// __Host-cdb_session.
/// </para>
///
/// <para>
/// <strong>Anonimo</strong>: cliente storefront NAO esta autenticado nesta fase.
/// Tenant vem do slug na rota.
/// </para>
/// </summary>
[SwaggerTag("Storefront Authentication / Autenticacao do storefront")]
[ApiController]
[Route("api/storefront/{slug}/auth")]
[AllowAnonymous]
public sealed class AuthController(
    SolicitarOtpUseCase solicitarOtpUseCase,
    ILogger<AuthController> logger) : EasyStockControllerBase
{
    public sealed record SolicitarOtpRequest(
        [Required] string Telefone);

    public sealed record SolicitarOtpResponse(
        int ExpiresInSeconds);

    public sealed record RateLimitErrorResponse(
        int RetryAfterSeconds);

    /// <summary>
    /// Solicita codigo OTP para o telefone informado. Codigo de 6 digitos
    /// numericos e enviado via WhatsApp (provider real em prod, stub em dev).
    ///
    /// <para>
    /// Idempotencia: mesmo telefone em &lt;60s reaproveita o codigo emitido
    /// (sem novo envio). Util para double-tap do botao "Reenviar".
    /// </para>
    ///
    /// <para>
    /// Rate limit anti-abuso: max 3 OTPs/hora por telefone. 4a chamada retorna
    /// 429 com <c>retryAfterSeconds</c>.
    /// </para>
    /// </summary>
    [SwaggerOperation(Summary = "Solicitar codigo OTP", Description = "Gera codigo de 6 digitos e envia via WhatsApp/SMS. Rate limit 3/hora/telefone. Idempotente em <60s.")]
    [ProducesResponseType(typeof(SolicitarOtpResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    [EnableRateLimiting("auth")]
    [HttpPost("solicitar-otp")]
    public async Task<IActionResult> SolicitarOtp(
        [FromRoute] string slug,
        [FromBody] SolicitarOtpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var idempotencyKey = Request.Headers.TryGetValue("X-Idempotency-Key", out var keyValues)
            ? keyValues.ToString()
            : null;
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        try
        {
            var result = await solicitarOtpUseCase.ExecuteAsync(new SolicitarOtpInput(
                Slug: slug,
                Telefone: request.Telefone,
                IdempotencyKey: string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
                IpOrigem: ip,
                UserAgent: string.IsNullOrWhiteSpace(ua) ? null : ua));

            return Accepted(new SolicitarOtpResponse(result.ExpiresInSeconds));
        }
        catch (TelefoneInvalidoException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (OtpRateLimitExcedidoException ex)
        {
            Response.Headers.Append("Retry-After", ex.RetryAfterSeconds.ToString());
            return StatusCode(
                StatusCodes.Status429TooManyRequests,
                new RateLimitErrorResponse(ex.RetryAfterSeconds));
        }
        catch (OtpProviderException ex)
        {
            // Provider externo falhou (WhatsApp Cloud API timeout/5xx). OTP foi
            // persistido — cliente pode tentar "Reenviar" que reaproveita o registro.
            logger.LogError(ex, "Falha no provider de OTP para slug={Slug}", slug);
            return StatusCode(StatusCodes.Status502BadGateway, new
            {
                error = new
                {
                    code = "OTP_PROVIDER_UNAVAILABLE",
                    message = "Nao foi possivel enviar o codigo agora. Tente novamente em instantes.",
                },
            });
        }
    }
}
