using EasyStock.Application.UseCases.Inventario.Desacertos;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Corpo de <c>POST api/estoque/desacertos/{produtoId}/ajustar</c>.</summary>
public sealed record AjustarSaldoRapidoRequest(decimal QuantidadeContada, string? Motivo, Guid? LojaId = null);

/// <summary>
/// Alerta de desacerto e ajuste rápido de saldo (S22, RN-48/RN-49, UC-09). A lista é projeção de
/// <c>QuantidadeDescoberta</c>; o ajuste reusa a contagem física e fecha o alerta por construção.
/// Exige <see cref="Permissao.GerenciarEstoque"/>.
/// </summary>
[SwaggerTag("Inventory / Estoque")]
[Authorize]
[ApiController]
[Route("api/estoque/desacertos")]
public sealed class EstoqueDesacertosController(
    ListarDesacertosEstoqueUseCase listar,
    AjustarSaldoRapidoUseCase ajustar,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Produtos vendidos sem saldo (descoberto > 0), com texto citando os pedidos")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] Guid? empresaId, [FromQuery] Guid? lojaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();

        var desacertos = await listar.ExecuteAsync(new ListarDesacertosEstoqueInput(emp, lojaId), ct);
        return DataOk(desacertos);
    }

    [SwaggerOperation(Summary = "Ajuste rápido: informa a contagem do produto, zera o descoberto e grava o ajuste")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost("{produtoId:guid}/ajustar")]
    public async Task<IActionResult> Ajustar(
        Guid produtoId, [FromBody] AjustarSaldoRapidoRequest request, [FromQuery] Guid? empresaId, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();

        var resultado = await ajustar.ExecuteAsync(new AjustarSaldoRapidoInput(
            emp, currentUser.UsuarioId, produtoId, request.LojaId, request.QuantidadeContada, request.Motivo ?? string.Empty), ct);
        return DataOk(resultado);
    }
}
