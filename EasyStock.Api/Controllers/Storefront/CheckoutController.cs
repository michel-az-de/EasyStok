using EasyStock.Application.Services.Storefront;
using EasyStock.Application.UseCases.Atendimento.ChatSite;
using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Checkout.Idempotency;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.AspNetCore.RateLimiting;
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
        catch (Exception ex) when (TentarMapearErro(ex, out var resposta))
        {
            return resposta;
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
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    [EnableRateLimiting("public-post")]
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
        catch (Exception ex) when (TentarMapearErro(ex, out var resposta))
        {
            return resposta;
        }
    }

    /// <summary>
    /// Tradução única exceção -> resposta HTTP dos dois checkouts (logado e guest; estrutura-12). Antes cada um tinha
    /// a sua cópia, e a mesma exceção saía em dois formatos. Devolve false para a exceção que não é daqui, que segue
    /// para o handler global. Corpo e status de cada erro são os que o site já recebia; o que mudava era o
    /// "carrinho alterado" (ProblemDetails no logado, envelope <c>error</c> no guest), que agora leva os dois.
    /// </summary>
    [NonAction]
    public bool TentarMapearErro(Exception ex, out IActionResult resposta)
    {
        switch (ex)
        {
            case IdempotencyMismatchException:
                var carrinho = Problema(StatusCodes.Status409Conflict, "Carrinho alterado", ex.Message);
                carrinho.Type = "https://httpstatuses.com/409";
                carrinho.Extensions["error"] = new { code = "CARRINHO_ALTERADO", message = ex.Message };
                resposta = StatusCode(StatusCodes.Status409Conflict, carrinho);
                return true;
            case CheckoutEmAndamentoException:
                Response.Headers["Retry-After"] = "2";
                resposta = StatusCode(409, new { error = new { code = "CHECKOUT_EM_ANDAMENTO", message = ex.Message } });
                return true;
            case SessaoChatSiteInvalidaException:
                resposta = StatusCode(403, new { error = new { code = "SESSAO_CHAT_INVALIDA", message = ex.Message } });
                return true;
            case ChatSiteIndisponivelException or StorefrontNaoEncontradoException:
                resposta = DataNotFound(ex.Message);
                return true;
            case CepInvalidoException or UseCaseValidationException:
                resposta = DataBadRequest(ex.Message);
                return true;
            case LojaFechadaException:
                // S40: a dona fechou a loja na mão; a mensagem é a de "loja fechada" configurada.
                resposta = Resposta(StatusCodes.Status409Conflict, "Loja fechada", ex.Message);
                return true;
            case JanelaSemVagasException:
                resposta = Resposta(StatusCodes.Status409Conflict, "Janela esgotada", ex.Message);
                return true;
            case CepSemCoberturaException:
                resposta = Resposta(StatusCodes.Status422UnprocessableEntity, "CEP sem cobertura", ex.Message);
                return true;
            case MercadoPagoIndisponivelException:
                resposta = Resposta(StatusCodes.Status503ServiceUnavailable, "Gateway de pagamento indisponível", ex.Message);
                return true;
            case TelefoneInvalidoException:
                resposta = Resposta(StatusCodes.Status400BadRequest, "Telefone invalido", ex.Message);
                return true;
            case RegraDeDominioVioladaException:
                resposta = Resposta(StatusCodes.Status422UnprocessableEntity, "Dados inválidos", ex.Message);
                return true;
            default:
                resposta = null!;
                return false;
        }
    }

    private static ProblemDetails Problema(int status, string titulo, string detalhe) =>
        new() { Status = status, Title = titulo, Detail = detalhe };

    private IActionResult Resposta(int status, string titulo, string detalhe) =>
        StatusCode(status, Problema(status, titulo, detalhe));

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
