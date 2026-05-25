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
/// Endpoints públicos de autenticação do storefront (ADR-0012).
///
/// <para>
/// <strong>Fluxo</strong>: cliente fornece telefone → POST /solicitar-otp → recebe
/// código via WhatsApp/SMS → POST /validar-otp → recebe cookie __Host-cdb_session.
/// </para>
///
/// <para>
/// <strong>Anônimo</strong>: cliente storefront NÃO está autenticado nesta fase.
/// Tenant vem do slug na rota.
/// </para>
/// </summary>
[SwaggerTag("Storefront Authentication / Autenticação do storefront")]
[ApiController]
[Route("api/storefront/{slug}/auth")]
[AllowAnonymous]
public sealed class AuthController(
    ValidarOtpUseCase validarOtpUseCase) : EasyStockControllerBase
{
    public sealed record ValidarOtpRequest(
        [Required] string Telefone,
        [Required] string Codigo);

    public sealed record ValidarOtpResponse(
        string TelefoneOfuscado,
        string PrimeiroNome);

    public sealed record RateLimitErrorResponse(
        string Mensagem);

    /// <summary>
    /// Valida código OTP recebido via WhatsApp e inicia sessão server-side.
    ///
    /// <para>
    /// Em caso de sucesso, seta o cookie <c>__Host-cdb_session</c> (HttpOnly, Secure,
    /// SameSite=Lax, Max-Age=30d) contendo o ID da sessão.
    /// </para>
    ///
    /// <para>
    /// Máximo de 5 tentativas por OTP. Na 5ª tentativa incorreta, o OTP é
    /// invalidado e o cliente deve solicitar novo código via /solicitar-otp.
    /// </para>
    ///
    /// <para>
    /// Respostas de erro (código errado vs telefone inexistente) usam a mesma
    /// mensagem genérica — anti-enumeração.
    /// </para>
    /// </summary>
    [SwaggerOperation(
        Summary = "Validar código OTP",
        Description = "Verifica código de 6 dígitos recebido via WhatsApp. Em sucesso seta cookie de sessão __Host-cdb_session.")]
    [ProducesResponseType(typeof(ValidarOtpResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [EnableRateLimiting("auth")]
    [HttpPost("validar-otp")]
    public async Task<IActionResult> ValidarOtp(
        [FromRoute] string slug,
        [FromBody] ValidarOtpRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();
        var al = Request.Headers["Accept-Language"].ToString();

        try
        {
            var result = await validarOtpUseCase.ExecuteAsync(new ValidarOtpInput(
                Slug: slug,
                Telefone: request.Telefone,
                Codigo: request.Codigo,
                IpOrigem: ip,
                UserAgent: string.IsNullOrWhiteSpace(ua) ? null : ua,
                AcceptLanguage: string.IsNullOrWhiteSpace(al) ? null : al), ct);

            Response.Cookies.Append("__Host-cdb_session", result.SessionId.ToString(), new CookieOptions
            {
                HttpOnly = true,
                Secure = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromSeconds(result.MaxAgeSecs),
                Path = "/",
            });

            return Ok(new ValidarOtpResponse(result.TelefoneOfuscado, result.PrimeiroNome));
        }
        catch (TelefoneInvalidoException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (OtpExpiradoException ex)
        {
            return StatusCode(StatusCodes.Status410Gone, new { error = new { code = "OTP_EXPIRADO", message = ex.Message } });
        }
        catch (OtpTentativasExcedidasException ex)
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new RateLimitErrorResponse(ex.Message));
        }
        catch (OtpInvalidoException ex)
        {
            return Unauthorized(new { error = new { code = "OTP_INVALIDO", message = ex.Message } });
        }
    }
}
