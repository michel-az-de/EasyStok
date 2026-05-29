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
/// <strong>Auth:</strong> reuso do <c>[Authorize]</c> padrão do ERP — staff Babá já
/// autenticada via Identity. Não usa cookie <c>__Host-cdb_session</c> (esse é do cliente
/// storefront). <c>UsuarioId</c> e <c>EmpresaId</c> vêm do <see cref="ICurrentUserAccessor"/>.
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
[Authorize]
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
    [ProducesResponseType(typeof(AprovarPedidoStorefrontResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Observações acima do limite",
                    Detail = "observacoes deve ter no máximo 500 caracteres.",
                });
        }

        var input = new AprovarPedidoStorefrontInput(
            PedidoId: id,
            EmpresaId: empresaId,
            UsuarioId: usuarioId,
            UsuarioNome: ObterNomeUsuario(),
            Observacoes: body?.Observacoes);

        try
        {
            var result = await aprovarUseCase.ExecuteAsync(input, ct);
            return Ok(result);
        }
        catch (PedidoNaoEncontradoException)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://easystok.app/errors/pedido-nao-encontrado",
                Status = StatusCodes.Status404NotFound,
                Title = "Pedido não encontrado",
            });
        }
        catch (PedidoJaResolvidoException ex)
        {
            return Conflict(new PedidoJaResolvidoProblemDetails
            {
                Type = "https://easystok.app/errors/pedido-ja-resolvido",
                Status = StatusCodes.Status409Conflict,
                Title = "Pedido já resolvido",
                Detail = ex.Message,
                StatusAtual = ex.StatusAtualString,
                ResolvidoEm = ex.ResolvidoEm,
            });
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
    [ProducesResponseType(typeof(RecusarPedidoStorefrontResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
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
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Body obrigatório",
                    Detail = "Body do POST /recusar não pode ser vazio (motivo é obrigatório).",
                });
        }

        if (!MotivoRecusaExtensions.TryParse(body.Motivo, out var motivo))
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Motivo inválido",
                    Detail = $"motivo deve ser um de: ESTOQUE_INSUFICIENTE, OPERACIONAL, OUTRO. Recebido: '{body.Motivo}'.",
                });
        }

        if (body.MensagemCliente is { Length: > 280 })
        {
            return StatusCode(
                StatusCodes.Status422UnprocessableEntity,
                new ProblemDetails
                {
                    Status = StatusCodes.Status422UnprocessableEntity,
                    Title = "Mensagem ao cliente acima do limite",
                    Detail = "mensagemCliente deve ter no máximo 280 caracteres.",
                });
        }

        var input = new RecusarPedidoStorefrontInput(
            PedidoId: id,
            EmpresaId: empresaId,
            UsuarioId: usuarioId,
            Motivo: motivo,
            MensagemCliente: body.MensagemCliente,
            UsuarioNome: ObterNomeUsuario());

        try
        {
            var result = await recusarUseCase.ExecuteAsync(input, ct);
            return Ok(result);
        }
        catch (PedidoNaoEncontradoException)
        {
            return NotFound(new ProblemDetails
            {
                Type = "https://easystok.app/errors/pedido-nao-encontrado",
                Status = StatusCodes.Status404NotFound,
                Title = "Pedido não encontrado",
            });
        }
        catch (PedidoJaResolvidoException ex)
        {
            return Conflict(new PedidoJaResolvidoProblemDetails
            {
                Type = "https://easystok.app/errors/pedido-ja-resolvido",
                Status = StatusCodes.Status409Conflict,
                Title = "Pedido já resolvido",
                Detail = ex.Message,
                StatusAtual = ex.StatusAtualString,
                ResolvidoEm = ex.ResolvidoEm,
            });
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
            error = Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Autenticação requerida",
                Detail = "Usuário Babá precisa estar autenticado para aprovar/recusar pedidos.",
            });
            return false;
        }

        return true;
    }

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

/// <summary>ProblemDetails extendido com <c>statusAtual</c> + <c>resolvidoEm</c> (contrato 409).</summary>
public sealed class PedidoJaResolvidoProblemDetails : ProblemDetails
{
    public string? StatusAtual { get; set; }
    public DateTime? ResolvidoEm { get; set; }
}
