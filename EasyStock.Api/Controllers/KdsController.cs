using EasyStock.Application.UseCases.AtualizarStatusPedido;
using EasyStock.Application.UseCases.Common;
using EasyStock.Application.UseCases.Operacao.Kds;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// KDS do console (S19) sobre <c>Pedido</c>, com JWT. O KDS antigo, sobre o espelho mobile e com
/// <c>X-Mobile-Api-Key</c>, ficou em <c>api/mobile/kds</c> até a P05. Mudança de status delega a
/// <see cref="AtualizarStatusPedidoUseCase"/> (máquina de estados, estoque, outbox e SSE da S18).
/// </summary>
[SwaggerTag("KDS (cozinha)")]
[ApiController]
[Route("api/kds")]
[Authorize(Policy = "Operador")]
[ValidateEmpresaId]
public class KdsController(
    ListarPedidosKdsUseCase listarUseCase,
    AtualizarStatusPedidoUseCase statusUseCase,
    AtualizarStatusPedidosEmLoteUseCase loteUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    private const string Origem = "kds";

    [SwaggerOperation(Summary = "Cards do KDS (padrão: aguardando, preparando, pronto e saiu para entrega de hoje e abertos de ontem)")]
    [HttpGet("pedidos")]
    public async Task<IActionResult> GetPedidos(
        [FromQuery] string? status,
        [FromQuery] string? linha,
        [FromQuery] DateOnly? data,
        [FromQuery] Guid? empresaId,
        CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var cards = await listarUseCase.ExecuteAsync(new ListarPedidosKdsQuery(emp, status, linha, data), ct);
        return DataOk(cards);
    }

    [SwaggerOperation(Summary = "Muda o status de um pedido (um toque no card)")]
    [HttpPatch("pedidos/{id:guid}/status")]
    public async Task<IActionResult> AtualizarStatus(Guid id, [FromBody] KdsAtualizarStatusRequest request)
    {
        if (!TryResolveEmpresaId(currentUser, request.EmpresaId, out var emp, out var err)) return err!;
        try
        {
            var result = await statusUseCase.ExecuteAsync(new AtualizarStatusPedidoCommand(
                emp, id, request.Status, UsuarioIdAtual(), Origem: Origem, NivelSolicitante: currentUser.Nivel));
            return result is null ? DataNotFound("Pedido não encontrado.") : DataOk(result);
        }
        catch (UseCaseValidationException ex)
        {
            // Transição inválida chega aqui com a mensagem da TransicaoInvalidaException.
            return DataBadRequest(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Marcação em lote quando a internet volta: aplica em sequência e reporta por item")]
    [HttpPost("pedidos/status-lote")]
    public async Task<IActionResult> AtualizarStatusEmLote(
        [FromBody] IReadOnlyList<KdsStatusLoteItem> itens,
        [FromQuery] Guid? empresaId = null,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        if (itens is null || itens.Count == 0) return DataBadRequest("Lote vazio.");
        if (itens.Count > AtualizarStatusPedidosEmLoteUseCase.MaximoItens)
            return DataBadRequest($"Lote acima de {AtualizarStatusPedidosEmLoteUseCase.MaximoItens} itens.");

        var resultados = await loteUseCase.ExecuteAsync(
            new AtualizarStatusPedidosEmLoteCommand(emp, itens, UsuarioIdAtual(), Origem, currentUser.Nivel), ct);
        return DataOk(resultados);
    }

    private Guid? UsuarioIdAtual() => currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null;
}

/// <param name="EmpresaId">Só para SuperAdmin; os demais usam a empresa do token.</param>
public sealed record KdsAtualizarStatusRequest(string Status, Guid? EmpresaId = null);
