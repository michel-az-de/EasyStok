using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Exceptions;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoint autenticado de checkout Storefront — protocolo 3 fases (ADR-0014).
///
/// <para>
/// Exige cookie <c>__Host-cdb_session</c> com o <c>sid</c> (Guid) da sessão ativa.
/// Sem cookie ou sessão inválida → 401. Tenant resolvido via slug na rota.
/// </para>
/// </summary>
[SwaggerTag("Storefront Checkout")]
[ApiController]
[Route("api/storefront/{slug}/checkout")]
[AllowAnonymous]
public sealed class CheckoutController(
    IniciarCheckoutUseCase iniciarCheckoutUseCase,
    IClienteSessionRepository clienteSessionRepository,
    TimeProvider timeProvider) : EasyStockControllerBase
{
    private const string SessionCookieName = "__Host-cdb_session";

    /// <summary>
    /// Inicia checkout: valida sessão, cria pedido (3 fases), retorna init_point MP.
    /// </summary>
    [SwaggerOperation(
        Summary = "Iniciar checkout",
        Description = "Cria Pedido (Rascunho → AguardandoPagamento) e retorna URL de pagamento MercadoPago. " +
                      "Exige cookie de sessão __Host-cdb_session. Idempotente via X-Idempotency-Key (UUID).")]
    [ProducesResponseType(typeof(CheckoutCriadoDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost]
    public async Task<IActionResult> IniciarCheckout(
        [FromRoute] string slug,
        [FromBody] CheckoutRequestBody body,
        CancellationToken ct)
    {
        // ── Auth: validar sessão via cookie ──────────────────────────────
        var sessionId = ObterSessionId();
        if (sessionId is null)
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Sessão necessária",
                Detail = "Cookie __Host-cdb_session ausente. Faça login via OTP.",
            });

        var session = await clienteSessionRepository.GetByIdAsync(sessionId.Value, ct);
        if (session is null || !session.EstaValida(timeProvider))
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Sessão inválida ou expirada",
                Detail = "Sessão não encontrada ou expirada. Refaça o login.",
            });

        // ── Idempotency headers (opcionais, mas X-Content-Hash obrigatório com Key) ──
        Guid? idempotencyKey = null;
        string? contentHash = null;

        if (Request.Headers.TryGetValue("X-Idempotency-Key", out var keyHeader)
            && Guid.TryParse(keyHeader.ToString(), out var parsedKey))
        {
            idempotencyKey = parsedKey;

            if (!Request.Headers.TryGetValue("X-Content-Hash", out var hashHeader)
                || string.IsNullOrWhiteSpace(hashHeader.ToString()))
            {
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Header ausente",
                    Detail = "X-Content-Hash é obrigatório quando X-Idempotency-Key está presente.",
                });
            }

            contentHash = hashHeader.ToString().Trim();
        }

        // ── Executar use case ─────────────────────────────────────────────
        var input = new IniciarCheckoutInput(
            Slug: slug,
            ClienteId: session.ClienteId,
            Items: body.Items.Select(i => new CheckoutItemInput(i.CardapioItemId, i.Qtd)).ToList(),
            JanelaId: body.JanelaId,
            DataEntrega: body.DataEntrega,
            Cep: body.Cep,
            Observacoes: body.Observacoes,
            IdempotencyKey: idempotencyKey,
            ContentHash: contentHash);

        try
        {
            var result = await iniciarCheckoutUseCase.ExecuteAsync(input, ct);
            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue { NoStore = true, NoCache = true };
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (IdempotencyMismatchException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Carrinho alterado",
                    Detail = ex.Message,
                    Type = "https://httpstatuses.com/409",
                });
        }
        catch (CepInvalidoException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (JanelaSemVagasException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Janela esgotada",
                    Detail = ex.Message,
                });
        }
        catch (CepSemCoberturaException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "CEP sem cobertura",
                    Detail = ex.Message,
                });
        }
        catch (MercadoPagoIndisponivelException ex)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "Gateway de pagamento indisponível",
                    Detail = ex.Message,
                });
        }
        catch (RegraDeDominioVioladaException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Dados inválidos",
                    Detail = ex.Message,
                });
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid? ObterSessionId()
    {
        if (!Request.Cookies.TryGetValue(SessionCookieName, out var cookieValue)
            || string.IsNullOrWhiteSpace(cookieValue))
            return null;

        // Aceita o cookie como Guid direto (testes) ou extrai o "sid" de um JWT simples.
        if (Guid.TryParse(cookieValue, out var guid))
            return guid;

        // Parsing minimal de JWT (sem validação de assinatura — middleware dedicado
        // faz isso em produção quando TASK-EZ-AUTH-002 for integrado).
        try
        {
            var parts = cookieValue.Split('.');
            if (parts.Length >= 2)
            {
                var payloadJson = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(PadBase64(parts[1])));
                using var doc = System.Text.Json.JsonDocument.Parse(payloadJson);
                if (doc.RootElement.TryGetProperty("sid", out var sidEl)
                    && Guid.TryParse(sidEl.GetString(), out var sidGuid))
                    return sidGuid;
            }
        }
        catch { /* JWT malformado — retorna null */ }

        return null;
    }

    private static string PadBase64(string base64)
    {
        return (base64.Length % 4) switch
        {
            2 => base64 + "==",
            3 => base64 + "=",
            _ => base64,
        };
    }
}

/// <summary>Body do POST /checkout.</summary>
public sealed record CheckoutRequestBody(
    IReadOnlyList<CheckoutItemRequestBody> Items,
    Guid JanelaId,
    DateOnly DataEntrega,
    string Cep,
    string? Observacoes = null);

public sealed record CheckoutItemRequestBody(Guid CardapioItemId, int Qtd);
