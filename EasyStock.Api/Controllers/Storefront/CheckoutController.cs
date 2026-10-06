using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Domain.Exceptions.Storefront;
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
[TenantDoStorefront]
public sealed class CheckoutController(
    CheckoutSiteUseCase checkoutSite,
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

            contentHash = Request.Headers["X-Content-Hash"].ToString();
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
            ContentHash: contentHash, Numero: body.Endereco?.Numero ?? body.Numero);

        try
        {
            var result = await checkoutSite.LogadoAsync(input, body.Endereco, Request.Headers["X-Chat-Token"].ToString(), idempotencyKey, ct);
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
        catch (CheckoutEmAndamentoException ex)
        {
            Response.Headers["Retry-After"] = "2";
            return StatusCode(409, new { error = new { code = "CHECKOUT_EM_ANDAMENTO", message = ex.Message } });
        }
        catch (SessaoChatSiteInvalidaException ex)
        {
            return StatusCode(403, new { error = new { code = "SESSAO_CHAT_INVALIDA", message = ex.Message } });
        }
        catch (ChatSiteIndisponivelException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (CepInvalidoException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (LojaFechadaException ex)
        {
            // S40: a dona fechou a loja na mão; a mensagem é a de "loja fechada" configurada.
            return StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Loja fechada",
                    Detail = ex.Message,
                });
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

    /// <summary>
    /// Inicia checkout GUEST (sem login): reserva a vaga da janela, cria o pedido em
    /// <c>aguardando_pagamento</c> e devolve o link do Mercado Pago, o token de acompanhamento e o
    /// numero curto (#1254). Mesmos codigos de erro do checkout logado.
    /// </summary>
    [SwaggerOperation(
        Summary = "Iniciar checkout guest (sem login)",
        Description = "Cria Pedido sem cookie de sessao, reserva a janela e retorna URL de pagamento " +
                      "MercadoPago. Telefone informado não concede identidade nem histórico verificado.")]
    [ProducesResponseType(typeof(IniciarCheckoutGuestResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [HttpPost("guest")]
    public async Task<IActionResult> IniciarCheckoutGuest(
        [FromRoute] string slug,
        [FromBody] CheckoutGuestRequestBody body,
        CancellationToken ct)
    {
        if (body is null)
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Body ausente",
                Detail = "Corpo da requisicao e obrigatorio.",
            });

        var input = new IniciarCheckoutGuestInput(
            Slug: slug,
            Nome: body.Nome,
            Telefone: body.Telefone,
            Cep: body.Cep,
            Numero: body.Numero,
            Items: (body.Items ?? Array.Empty<CheckoutItemRequestBody>())
                .Select(i => new CheckoutItemInput(i.CardapioItemId, i.Qtd))
                .ToList(),
            JanelaId: body.JanelaId,
            DataEntrega: body.DataEntrega,
            Observacoes: body.Observacoes);

        try
        {
            var result = await checkoutSite.GuestAsync(input, body.Endereco, Request.Headers["X-Chat-Token"].ToString(),
                Guid.TryParse(Request.Headers["X-Idempotency-Key"], out var key) ? key : null, ct);
            Response.GetTypedHeaders().CacheControl =
                new CacheControlHeaderValue { NoStore = true, NoCache = true };
            return StatusCode(StatusCodes.Status201Created, result);
        }
        catch (IdempotencyMismatchException ex)
        {
            return StatusCode(409, new { error = new { code = "CARRINHO_ALTERADO", message = ex.Message } });
        }
        catch (TelefoneInvalidoException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Telefone invalido",
                Detail = ex.Message,
            });
        }
        catch (CheckoutEmAndamentoException ex)
        {
            Response.Headers["Retry-After"] = "2";
            return StatusCode(409, new { error = new { code = "CHECKOUT_EM_ANDAMENTO", message = ex.Message } });
        }
        catch (SessaoChatSiteInvalidaException ex)
        {
            return StatusCode(403, new { error = new { code = "SESSAO_CHAT_INVALIDA", message = ex.Message } });
        }
        catch (ChatSiteIndisponivelException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (CepInvalidoException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (LojaFechadaException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Loja fechada", Detail = ex.Message });
        }
        catch (JanelaSemVagasException ex)
        {
            return StatusCode(
                StatusCodes.Status409Conflict,
                new ProblemDetails { Status = StatusCodes.Status409Conflict, Title = "Janela esgotada", Detail = ex.Message });
        }
        catch (CepSemCoberturaException ex)
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails { Status = StatusCodes.Status422UnprocessableEntity, Title = "CEP sem cobertura", Detail = ex.Message });
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
                    Title = "Dados invalidos",
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

        return Guid.TryParse(cookieValue, out var guid) ? guid : null;
    }

}

/// <summary>Body do POST /checkout.</summary>
public sealed record CheckoutRequestBody(
    IReadOnlyList<CheckoutItemRequestBody> Items,
    Guid JanelaId,
    DateOnly DataEntrega,
    string Cep,
    string? Observacoes = null,
    EnderecoCheckout? Endereco = null,
    string? Numero = null);

public sealed record CheckoutItemRequestBody(Guid CardapioItemId, int Qtd);

/// <summary>Body do POST /checkout/guest (issues #680 e #1254: janela e data obrigatórias).</summary>
public sealed record CheckoutGuestRequestBody(
    string Nome,
    string Telefone,
    string Cep,
    string? Numero,
    IReadOnlyList<CheckoutItemRequestBody> Items,
    Guid? JanelaId = null,
    DateOnly? DataEntrega = null,
    string? Observacoes = null,
    EnderecoCheckout? Endereco = null);
