using EasyStock.Application.UseCases.Pedidos.Cobranca;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record TrocarFormaPagamentoRequest(string Forma);

public sealed record DesfazerPagamentoManualRequest(string Motivo, Guid? PagamentoId = null);

/// <summary>
/// Cobrança do pedido pela operadora (S11): reemitir o link, trocar a forma de pagamento e desfazer
/// pagamento registrado à mão. Mesma rota de <see cref="PedidosController"/> (<c>api/pedidos</c>), em
/// controller próprio para não inflar o construtor daquele.
///
/// <para>
/// Erros: 404 pedido de outro tenant ou inexistente; 409 com <c>error.code</c> =
/// <c>pedido_ja_pago</c>, <c>use_estorno</c>, <c>preparo_iniciado</c>, <c>pedido_finalizado</c>,
/// <c>sem_pagamento</c> ou <c>forma_na_entrega</c>; 503 Mercado Pago indisponível.
/// </para>
/// </summary>
[SwaggerTag("Pedidos (encomendas)")]
[ApiController]
[Route("api/pedidos")]
[Authorize]
[ValidateEmpresaId]
public sealed class PedidosCobrancaController(
    GerarCobrancaPedidoUseCase gerarCobranca,
    TrocarFormaPagamentoPedidoUseCase trocarForma,
    DesfazerPagamentoManualUseCase desfazerPagamento,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Reemitir o link de pagamento do pedido (Mercado Pago)")]
    [HttpPost("{id}/cobranca")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> Reemitir(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        ExecutarAsync(empresaId, async emp =>
            DataOk(await gerarCobranca.ExecuteAsync(new GerarCobrancaPedidoInput(emp, id), ct)));

    [SwaggerOperation(Summary = "Trocar a forma de pagamento do pedido (online | na_entrega)")]
    [HttpPost("{id}/cobranca/forma")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> TrocarForma(
        Guid id, [FromBody] TrocarFormaPagamentoRequest request, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        ExecutarAsync(empresaId, async emp =>
            DataOk(await trocarForma.ExecuteAsync(
                new TrocarFormaPagamentoPedidoInput(emp, id, request.Forma, UsuarioId(), null), ct)));

    [SwaggerOperation(Summary = "Desfazer pagamento registrado à mão (motivo obrigatório)")]
    [HttpPost("{id}/pagamento-manual/desfazer")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> DesfazerPagamentoManual(
        Guid id, [FromBody] DesfazerPagamentoManualRequest request, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        ExecutarAsync(empresaId, async emp =>
            DataOk(await desfazerPagamento.ExecuteAsync(
                new DesfazerPagamentoManualInput(emp, id, request.Motivo, UsuarioId(), null, request.PagamentoId), ct)));

    private Guid? UsuarioId() => currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null;

    private async Task<IActionResult> ExecutarAsync(Guid? empresaId, Func<Guid, Task<IActionResult>> acao)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        try
        {
            return await acao(emp);
        }
        catch (CobrancaPedidoNaoEncontradoException)
        {
            return DataNotFound("Pedido não encontrado.");
        }
        catch (CobrancaPedidoConflitoException ex)
        {
            return Conflict(new ApiErrorResponse(new ApiError(ex.Codigo, ex.Message, null, null)));
        }
        catch (MercadoPagoIndisponivelException ex)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new ApiErrorResponse(new ApiError("pagamento_indisponivel", ex.Message, null, null)));
        }
    }
}
