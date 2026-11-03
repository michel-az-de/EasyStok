using EasyStock.Application.UseCases.Atendimento.Frete;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/clientes/{clienteId:guid}/viagem")]
[Authorize(Policy = "Operador")]
public sealed class ClientesViagemController(ConsultarViagemClienteUseCase viagens, ICurrentUserAccessor usuario) : EasyStockControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Consultar(Guid clienteId, [FromQuery] Guid pedidoId, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (usuario.EmpresaId == Guid.Empty || (empresaId.HasValue && empresaId != usuario.EmpresaId))
            return DataBadRequest("A empresa informada difere da sessão.");
        // O contexto da ficha exige Atendimento; ver endereços de outras paradas também exige Entregas.
        if (!usuario.TemPermissao(Permissao.AcessarModuloEntregas)) return Forbid();
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        var resultado = await viagens.ExecuteAsync(usuario.EmpresaId, clienteId, pedidoId, ct);
        return resultado is null ? DataNotFound("Pedido não encontrado para este cliente.") : DataOk(resultado);
    }
}
