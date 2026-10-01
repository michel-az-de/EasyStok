using System.Security.Claims;
using EasyStock.Application.UseCases.Storefront.Aprovacao;
using EasyStock.Application.UseCases.Storefront.Aprovacao.Exceptions;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers.Storefront;

/// <summary>
/// Endpoints autenticados de aprovação/recusa de pedido Storefront (TASK-EZ-APROVAR-001,
/// Fase 6 do plano v8.0 / ADR-0014).
///
/// <para>
/// <strong>Auth:</strong> policy <c>Operador</c> (#1238): <c>Visualizador</c> não aprova nem recusa, e
/// recusar dispara estorno. Não usa cookie <c>__Host-cdb_session</c> (esse é do cliente
/// storefront). <c>UsuarioId</c> e <c>EmpresaId</c> vêm do <see cref="ICurrentUserAccessor"/>.
/// </para>
///
/// <para>
/// <strong>Resposta:</strong> envelope padrão da Api (#1238), que o console e o Web desembrulham:
/// sucesso em <c>{ data, meta }</c>, erro em <c>{ error: { code, message, details } }</c>.
/// </para>
///
/// <para>
/// <strong>Tenant isolation:</strong> pedido de outro <c>EmpresaId</c> → 404 (não 403)
/// — evita oracle de existência cross-tenant. Contrato canônico:
/// <c>docs/multi-agent/contracts/aprovar-pedido.contract.md</c>.
/// </para>
/// </summary>
[SwaggerTag("Storefront Aprovação Pedido")]
[ApiController]
[Route("api/storefront/pedidos")]
[Authorize(Policy = "Operador")]
public sealed class AprovacaoPedidoController(
    AprovarPedidoStorefrontUseCase aprovarUseCase,
    RecusarPedidoStorefrontUseCase recusarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// Aprova o pedido — transição <c>AguardandoAprovacaoBaba → AprovadoBaba</c>
    /// com <c>SELECT FOR UPDATE</c> + Outbox <c>NotificarClientePedidoAprovadoEvent</c>.
    /// </summary>
    [SwaggerOperation(
        Summary = "Aprovar pedido storefront",
        Description = "Lock pessimista no pedido + transição AguardandoAprovacaoBaba → AprovadoBaba. " +
                      "Enfileira NotificarClientePedidoAprovadoEvent no Outbox (WhatsApp). " +
                      "Concorrência: 2 babás simultâneas → 1 sucesso (200), 1 falha (409).")]
    [ProducesResponseType(typeof(ApiResponse<AprovarPedidoStorefrontResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [HttpPost("{id:guid}/aprovar")]
    public async Task<IActionResult> Aprovar(
        [FromRoute] Guid id,
        [FromBody] AprovarPedidoRequestBody? body,
        CancellationToken ct)
    {
        if (!ValidarUsuario(out var usuarioId, out var empresaId, out var authErr))
            return authErr!;

        if (body?.Observacoes is { Length: > 500 })
            return DataUnprocessable("As observações devem ter no máximo 500 caracteres.");

        var input = new AprovarPedidoStorefrontInput(
            PedidoId: id,
            EmpresaId: empresaId,
            UsuarioId: usuarioId,
            UsuarioNome: ObterNomeUsuario(),
            Observacoes: body?.Observacoes);

        try
        {
            return DataOk(await aprovarUseCase.ExecuteAsync(input, ct));
        }
        catch (PedidoNaoEncontradoException)
        {
            return DataNotFound("Pedido não encontrado.");
        }
        catch (PedidoJaResolvidoException ex)
        {
            return PedidoJaResolvido(ex);
        }
    }

    /// <summary>
    /// Recusa o pedido — transição <c>AguardandoAprovacaoBaba → Cancelado</c>
    /// com <c>SELECT FOR UPDATE</c> + 3 eventos Outbox (cancelado, refund, notificação).
    /// </summary>
    [SwaggerOperation(
        Summary = "Recusar pedido storefront",
        Description = "Lock pessimista no pedido + transição AguardandoAprovacaoBaba → Cancelado. " +
                      "Enfileira PedidoCanceladoEvent (libera vaga), EstornarPagamentoAutomaticoEvent " +
                      "(refund MP via dispatcher TASK-EZ-APROVAR-002) e NotificarClientePagamentoRecusadoEvent.")]
    [ProducesResponseType(typeof(ApiResponse<RecusarPedidoStorefrontResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [HttpPost("{id:guid}/recusar")]
    public async Task<IActionResult> Recusar(
        [FromRoute] Guid id,
        [FromBody] RecusarPedidoRequestBody body,
        CancellationToken ct)
    {
        if (!ValidarUsuario(out var usuarioId, out var empresaId, out var authErr))
            return authErr!;

        if (body is null)
            return DataUnprocessable("Informe o motivo da recusa.");

        if (!MotivoRecusaExtensions.TryParse(body.Motivo, out var motivo))
            return DataUnprocessable(
                "Motivo inválido.",
                $"motivo deve ser um de: ESTOQUE_INSUFICIENTE, OPERACIONAL, OUTRO. Recebido: '{body.Motivo}'.");

        if (body.MensagemCliente is { Length: > 280 })
            return DataUnprocessable("A mensagem ao cliente deve ter no máximo 280 caracteres.");

        var input = new RecusarPedidoStorefrontInput(
            PedidoId: id,
            EmpresaId: empresaId,
            UsuarioId: usuarioId,
            Motivo: motivo,
            MensagemCliente: body.MensagemCliente,
            UsuarioNome: ObterNomeUsuario());

        try
        {
            return DataOk(await recusarUseCase.ExecuteAsync(input, ct));
        }
        catch (PedidoNaoEncontradoException)
        {
            return DataNotFound("Pedido não encontrado.");
        }
        catch (PedidoJaResolvidoException ex)
        {
            return PedidoJaResolvido(ex);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool ValidarUsuario(out Guid usuarioId, out Guid empresaId, out IActionResult? error)
    {
        usuarioId = currentUser.UsuarioId;
        empresaId = currentUser.EmpresaId;
        error = null;

        if (!currentUser.IsAuthenticated || usuarioId == Guid.Empty || empresaId == Guid.Empty)
        {
            error = Unauthorized(new ApiErrorResponse(new ApiError(
                "UNAUTHORIZED", "Entre de novo para aprovar ou recusar pedidos.", null, null)));
            return false;
        }

        return true;
    }

    private UnprocessableEntityObjectResult DataUnprocessable(string mensagem, string? detalhe = null) =>
        UnprocessableEntity(new ApiErrorResponse(new ApiError("VALIDATION_ERROR", mensagem, detalhe, null)));

    /// <summary>409 com o estado atual em <c>details</c> (contrato da corrida entre duas babás).</summary>
    private ConflictObjectResult PedidoJaResolvido(PedidoJaResolvidoException ex) =>
        Conflict(new ApiErrorResponse(new ApiError("PEDIDO_JA_RESOLVIDO", ex.Message, null, null)
        {
            Details = new { statusAtual = ex.StatusAtualString, resolvidoEm = ex.ResolvidoEm },
        }));

    private string? ObterNomeUsuario()
    {
        var nome = User.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(nome)) return nome;

        return User.FindFirstValue(ClaimTypes.Name)
               ?? User.FindFirstValue("name")
               ?? User.FindFirstValue(ClaimTypes.Email);
    }
}

/// <summary>Body do POST <c>/aprovar</c>.</summary>
public sealed record AprovarPedidoRequestBody(string? Observacoes);

/// <summary>Body do POST <c>/recusar</c>.</summary>
public sealed record RecusarPedidoRequestBody(string Motivo, string? MensagemCliente = null);
