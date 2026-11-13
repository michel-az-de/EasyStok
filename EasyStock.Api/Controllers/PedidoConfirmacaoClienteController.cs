using EasyStock.Application.UseCases.Atendimento.Avisos;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Controllers;

/// <summary>Botão "Cliente confirmou" da encomenda (#2096): marca ou desfaz a confirmação do cliente.</summary>
[ApiController]
[Route("api/pedidos")]
[Authorize(Policy = "Operador")]
[ValidateEmpresaId]
public sealed class PedidoConfirmacaoClienteController(
    ConfirmarEncomendaPeloClienteUseCase useCase, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpPost("{id:guid}/confirmacao-cliente")]
    public Task<IActionResult> Confirmar(Guid id, CancellationToken ct) =>
        Aplicar(empresaId => useCase.ConfirmarAsync(empresaId, id, ct));

    [HttpDelete("{id:guid}/confirmacao-cliente")]
    public Task<IActionResult> Desfazer(Guid id, CancellationToken ct) =>
        Aplicar(empresaId => useCase.DesfazerAsync(empresaId, id, ct));

    private async Task<IActionResult> Aplicar(Func<Guid, Task<ConfirmacaoEncomendaResult?>> acao)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var erro)) return erro!;
        try
        {
            return await acao(empresaId) is null ? DataNotFound("Pedido não encontrado.") : NoContent();
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}
