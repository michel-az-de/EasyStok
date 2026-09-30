using EasyStock.Application.UseCases.Producao;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// S23 (#1137): producao em porcoes. O POST cria o lote finalizado e a entrada de estoque
/// numa transacao; e idempotente pelo header Idempotency-Key (whitelist em
/// <see cref="Hosting.PipelineExtensions.ConfigurarRotasIdempotentes"/>).
/// </summary>
[SwaggerTag("Inventory / Producao")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/producao")]
public class ProducaoController(
    RegistrarProducaoUseCase registrarProducaoUseCase,
    IItemEstoqueRepository itemEstoqueRepository,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    private const int LimiteVencendo = 200;

    [SwaggerOperation(Summary = "Registra producao em porcoes (lote finalizado + entrada de estoque)")]
    [ProducesResponseType(typeof(RegistrarProducaoResult), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpPost]
    public async Task<IActionResult> Registrar(RegistrarProducaoCommand command, CancellationToken ct)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var empresaId, out var error))
            return error!;

        var result = await registrarProducaoUseCase.ExecuteAsync(command with
        {
            EmpresaId = empresaId,
            OperadorUserId = command.OperadorUserId ?? (currentUser.UsuarioId == Guid.Empty ? null : currentUser.UsuarioId)
        }, ct);
        return DataCreated($"/api/lotes/{result.LoteId}", result);
    }

    [SwaggerOperation(Summary = "Lotes de estoque com saldo vencendo nos proximos N dias (RN-51)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("lotes")]
    public async Task<IActionResult> LotesVencendo(
        [FromQuery] Guid empresaId,
        [FromQuery] int vencendoEmDias = 3,
        [FromQuery] Guid? lojaId = null)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;
        if (vencendoEmDias < 0)
            return DataBadRequest("vencendoEmDias nao pode ser negativo.");

        var (itens, _) = await itemEstoqueRepository.GetProximoVencimentoAsync(
            resolvedEmpresaId, vencendoEmDias, 1, LimiteVencendo, lojaId);

        var dtos = itens
            .Where(i => i.QuantidadeAtual.Value > 0)
            .Select(i => new
            {
                itemEstoqueId = i.Id,
                produtoId = i.ProdutoId,
                produto = i.Produto?.Nome,
                codigoLote = i.CodigoLote?.Value,
                validadeEm = i.ValidadeEm != null ? DateOnly.FromDateTime(i.ValidadeEm.DataValidade) : (DateOnly?)null,
                saldo = i.QuantidadeAtual.Value,
                lojaId = i.LojaId
            })
            .ToList();

        return DataOk(dtos);
    }
}
