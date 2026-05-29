using EasyStock.Application.UseCases.Faturas.ListarFaturasCliente;
using EasyStock.Application.UseCases.Faturas.ObterFaturaDetalhe;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoints cliente (self-service) para o modulo Financeiro. Filtra
/// automaticamente por <see cref="ICurrentUserAccessor.EmpresaId"/> — nao e
/// possivel listar/ver fatura de outra empresa.
/// </summary>
[SwaggerTag("Faturas (cliente self-service)")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/faturas")]
public class FaturasController(
    ListarFaturasClienteUseCase listarUseCase,
    ObterFaturaDetalheUseCase detalheUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Listar faturas da empresa atual")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar(
        [FromQuery] Guid? empresaId,
        [FromQuery] StatusFatura? status,
        [FromQuery] DateTime? vencimentoDe,
        [FromQuery] DateTime? vencimentoAte,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var result = await listarUseCase.ExecuteAsync(
            new ListarFaturasClienteCommand(eid, status, vencimentoDe, vencimentoAte, page, pageSize),
            ct);

        return DataPaged(result.Itens, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Obter detalhe de uma fatura")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Detalhe(
        Guid id,
        [FromQuery] Guid? empresaId,
        CancellationToken ct = default)
    {
        if (!RequerVisualizar(out var permErr)) return permErr!;
        if (!TryResolveEmpresaId(currentUser, empresaId, out var eid, out var err)) return err!;

        var result = await detalheUseCase.ExecuteAsync(
            new ObterFaturaDetalheCommand(eid, id, Admin: false),
            ct);

        // Retorna 404 (nao 403) quando fatura e de outra empresa — evita enumeration.
        if (result is null) return DataNotFound("Fatura nao encontrada.");

        return DataOk(result);
    }

    private bool RequerVisualizar(out IActionResult? error)
    {
        if (!currentUser.TemPermissao(Permissao.VisualizarFaturas))
        {
            error = Forbid();
            return false;
        }
        error = null;
        return true;
    }
}
