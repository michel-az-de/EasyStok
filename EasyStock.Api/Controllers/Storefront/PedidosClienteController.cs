using EasyStock.Application.Ports.Output.Persistence.Storefront;
using EasyStock.Application.UseCases.Storefront.Checkout;
using EasyStock.Application.UseCases.Storefront.Pedidos;
using EasyStock.Domain.Exceptions.Storefront;
using Microsoft.Net.Http.Headers;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoint autenticado de listagem de pedidos do cliente storefront
/// (TASK-EZ-PEDIDOS-001 — consumido por <c>casa-da-baba/meus-pedidos.html</c>).
///
/// <para>
/// Exige cookie <c>__Host-cdb_session</c> com o <c>sid</c> (Guid) da sessão ativa
/// — mesmo mecanismo do <see cref="CheckoutController"/>. Sem cookie ou sessão
/// inválida → 401. Tenant resolvido via slug na rota.
/// </para>
/// </summary>
[SwaggerTag("Storefront Pedidos")]
[ApiController]
[Route("api/storefront/{slug}/pedidos")]
[AllowAnonymous]
public sealed class PedidosClienteController(
    ListarPedidosClienteUseCase listarUseCase,
    ObterPedidoClienteUseCase obterUseCase,
    ObterPedidoGuestUseCase obterGuestUseCase,
    AcompanhamentoTokenService acompanhamentoTokenService,
    IClienteSessionRepository clienteSessionRepository,
    TimeProvider timeProvider) : EasyStockControllerBase
{
    private const string SessionCookieName = "__Host-cdb_session";

    /// <summary>
    /// Lista os pedidos do cliente autenticado, ordenados por CriadoEm DESC.
    /// MVP retorna no máximo 50 pedidos (clamp do use case).
    /// </summary>
    [SwaggerOperation(
        Summary = "Listar pedidos do cliente",
        Description = "Retorna lista de pedidos do cliente autenticado para o storefront. " +
                      "Exige cookie de sessão __Host-cdb_session.")]
    [ProducesResponseType(typeof(ListarPedidosClienteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromRoute] string slug,
        [FromQuery] int? limit,
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

        // ── Executar use case ─────────────────────────────────────────────
        try
        {
            var result = await listarUseCase.ExecuteAsync(
                new ListarPedidosClienteInput(
                    Slug: slug,
                    ClienteId: session.ClienteId,
                    Limit: limit),
                ct);

            // Cache: dado per-cliente, mutável a cada mudança de status.
            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
            {
                Private = true,
                NoStore = true,
            };
            return Ok(result);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
    }

    /// <summary>
    /// Obtém um pedido individual do cliente autenticado (tela de acompanhamento — issue #670).
    /// 404 se o pedido não existir OU não for do cliente (anti-enumeração).
    /// </summary>
    [SwaggerOperation(
        Summary = "Obter pedido (cliente logado OU guest via token)",
        Description = "Retorna um pedido. Logado: cookie __Host-cdb_session. " +
                      "Guest (#684): query param t=<jwt> assinado pelo AcompanhamentoTokenService.")]
    [ProducesResponseType(typeof(ObterPedidoClienteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{pedidoId:guid}")]
    public async Task<IActionResult> Obter(
        [FromRoute] string slug,
        [FromRoute] Guid pedidoId,
        [FromQuery] string? t,
        CancellationToken ct)
    {
        // ── Caminho GUEST (#684): token assinado autoriza posse do link ──
        if (!string.IsNullOrWhiteSpace(t))
        {
            try
            {
                acompanhamentoTokenService.Validar(t, pedidoId);
            }
            catch (AcompanhamentoTokenInvalidoException)
            {
                // Anti-enumeracao: token invalido vira 404, nao 401.
                return DataNotFound("Pedido não encontrado.");
            }

            try
            {
                var result = await obterGuestUseCase.ExecuteAsync(slug, pedidoId, ct);
                if (result is null)
                    return DataNotFound("Pedido não encontrado.");

                Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
                {
                    Private = true,
                    NoStore = true,
                };
                return Ok(result);
            }
            catch (StorefrontNaoEncontradoException ex)
            {
                return DataNotFound(ex.Message);
            }
        }

        // ── Caminho LOGADO: cookie de sessao (path antigo, inalterado) ────
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

        try
        {
            var result = await obterUseCase.ExecuteAsync(
                new ObterPedidoClienteInput(
                    Slug: slug,
                    ClienteId: session.ClienteId,
                    PedidoId: pedidoId),
                ct);

            // null = pedido inexistente OU de outro cliente → 404 (anti-enumeração).
            if (result is null)
                return DataNotFound("Pedido não encontrado.");

            Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue
            {
                Private = true,
                NoStore = true,
            };
            return Ok(result);
        }
        catch (StorefrontNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lê o cookie de sessão e extrai o sid (Guid). Aceita Guid direto (compat
    /// com testes/dev) ou extrai do payload "sid" de JWT (parsing minimal —
    /// validação de assinatura fica para middleware dedicado quando
    /// TASK-EZ-AUTH-002 for integrado). Espelha <see cref="CheckoutController"/>.
    /// </summary>
    private Guid? ObterSessionId()
    {
        if (!Request.Cookies.TryGetValue(SessionCookieName, out var cookieValue)
            || string.IsNullOrWhiteSpace(cookieValue))
            return null;

        if (Guid.TryParse(cookieValue, out var guid))
            return guid;

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

    private static string PadBase64(string base64) => (base64.Length % 4) switch
    {
        2 => base64 + "==",
        3 => base64 + "=",
        _ => base64,
    };
}
