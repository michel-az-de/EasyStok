using EasyStock.Application.UseCases.AlterarAgendamentoPedido;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/pedidos")]
[Authorize(Policy = "Operador")]
[ValidateEmpresaId]
public sealed class PedidoJanelaController(TrocarJanelaPedidoUseCase useCase, ICurrentUserAccessor currentUser)
    : EasyStockControllerBase
{
    [HttpGet("{id:guid}/janelas")]
    public async Task<IActionResult> Listar(Guid id, [FromQuery] DateOnly? dataInicio,
        [FromQuery] DateOnly? dataFim, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var erro)) return erro!;
        var resultado = await useCase.ListarAsync(empresaId, id, dataInicio, dataFim, ct);
        return resultado is null ? DataNotFound("Pedido não encontrado.") : DataOk(resultado);
    }

    [HttpPatch("{id:guid}/janela")]
    public async Task<IActionResult> Trocar(Guid id, [FromBody] TrocarJanelaBody body, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, body.EmpresaId, out var empresaId, out var erro)) return erro!;
        var resultado = await useCase.ExecuteAsync(new(empresaId, id, body.JanelaId, body.Data, body.AvisarCliente,
            currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId, User.Identity?.Name), ct);
        return resultado is null ? DataNotFound("Pedido não encontrado.") : DataOk(resultado);
    }
}

public sealed record TrocarJanelaBody(Guid JanelaId, DateOnly Data, bool AvisarCliente = false, Guid? EmpresaId = null);
