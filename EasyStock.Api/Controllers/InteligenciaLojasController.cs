using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Store Intelligence / Inteligência por Loja")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/inteligencia-lojas")]
[EnableRateLimiting("geral")]
public class InteligenciaLojasController(
    IAnalyticsRepository analyticsRepository,
    EasyStock.Application.Ports.Output.ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Cross-store comparison", Description = "Returns health score, KPIs and ranking for all active stores.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("comparacao")]
    public async Task<IActionResult> Comparacao(
        [FromQuery] Guid empresaId,
        [FromQuery] int periodo = 30)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await analyticsRepository.GetComparacaoLojasAsync(resolvedEmpresaId, periodo));
    }

    [SwaggerOperation(Summary = "Per-store intelligence summary", Description = "Operational health, KPIs and health score breakdown for a specific store.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{lojaId:guid}")]
    public async Task<IActionResult> Resumo(
        [FromRoute] Guid lojaId,
        [FromQuery] Guid empresaId,
        [FromQuery] int periodo = 30)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var resumo = await analyticsRepository.GetResumoInteligenciaLojaAsync(resolvedEmpresaId, lojaId, periodo);
        return resumo is null ? DataNotFound("Loja nao encontrada.") : DataOk(resumo);
    }

    [SwaggerOperation(Summary = "Top products by turnover in a store")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("{lojaId:guid}/top-produtos")]
    public async Task<IActionResult> TopProdutos(
        [FromRoute] Guid lojaId,
        [FromQuery] Guid empresaId,
        [FromQuery] int periodo = 30,
        [FromQuery] int top = 10)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await analyticsRepository.GetTopProdutosPorLojaAsync(resolvedEmpresaId, lojaId, periodo, top, ascending: false));
    }

    [SwaggerOperation(Summary = "Bottom products by turnover in a store (slowest movers)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("{lojaId:guid}/bottom-produtos")]
    public async Task<IActionResult> BottomProdutos(
        [FromRoute] Guid lojaId,
        [FromQuery] Guid empresaId,
        [FromQuery] int periodo = 30,
        [FromQuery] int top = 10)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await analyticsRepository.GetTopProdutosPorLojaAsync(resolvedEmpresaId, lojaId, periodo, top, ascending: true));
    }

    [SwaggerOperation(Summary = "Per-store expiry alerts (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("{lojaId:guid}/validade")]
    public async Task<IActionResult> Validade(
        [FromRoute] Guid lojaId,
        [FromQuery] Guid empresaId,
        [FromQuery] int dias = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(resolvedEmpresaId, dias, page, pageSize, lojaId);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Per-store replenishment suggestions (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("{lojaId:guid}/reposicao")]
    public async Task<IActionResult> Reposicao(
        [FromRoute] Guid lojaId,
        [FromQuery] Guid empresaId,
        [FromQuery] int diasHistorico = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await analyticsRepository.GetSugestaoReposicaoDetalhadaAsync(resolvedEmpresaId, diasHistorico, page, pageSize, lojaId);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Per-store idle/slow-moving items (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("{lojaId:guid}/parados")]
    public async Task<IActionResult> Parados(
        [FromRoute] Guid lojaId,
        [FromQuery] Guid empresaId,
        [FromQuery] int diasSemMovimento = 90,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        var (items, totalCount) = await analyticsRepository.GetItensParadosDetalhadosAsync(resolvedEmpresaId, diasSemMovimento, page, pageSize, lojaId);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Actionable indicators across stores", Description = "Returns prioritized action signals for all stores or a specific store.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("indicadores")]
    public async Task<IActionResult> Indicadores(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int periodo = 30)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var resolvedEmpresaId, out var error))
            return error!;

        return DataOk(await analyticsRepository.GetIndicadoresAcaoAsync(resolvedEmpresaId, periodo, lojaId));
    }
}
