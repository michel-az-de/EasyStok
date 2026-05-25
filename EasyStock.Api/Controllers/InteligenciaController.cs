using EasyStock.Api.Http;
using EasyStock.Application.UseCases.Inteligencia.Board;
using EasyStock.Application.UseCases.Inteligencia.ProjecaoRuptura;
using EasyStock.Application.UseCases.Inteligencia.Rotatividade;
using EasyStock.Application.UseCases.Inteligencia.Sazonalidade;
using EasyStock.Application.UseCases.Inteligencia.EstoqueBaixo;
using EasyStock.Application.UseCases.Inteligencia.ProximoVencimento;
using EasyStock.Application.UseCases.Inteligencia.ItensParados;
using EasyStock.Application.UseCases.Inteligencia.SugestaoReposicao;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Intelligence / Inteligência")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/inteligencia")]
[EnableRateLimiting("ai")]
public class InteligenciaController(
    GetBoardUseCase getBoardUseCase,
    CalcularProjecaoRupturaUseCase calcProjecaoRupturaUseCase,
    CalcularRotatividadeUseCase calcRotatividadeUseCase,
    CalcularSazonalidadeInteligenciaUseCase calcSazonalidadeUseCase,
    ObterEstoqueBaixoUseCase obterEstoqueBaixoUseCase,
    ObterProximoVencimentoUseCase obterProximoVencimentoUseCase,
    ObterItensParadosUseCase obterItensParadosUseCase,
    ObterSugestaoReposicaoUseCase obterSugestaoReposicaoUseCase) : EasyStockControllerBase
{

    [SwaggerOperation(Summary = "Get low-stock items")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("estoque-baixo")]
    public async Task<IActionResult> EstoqueBaixo(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? limite,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await obterEstoqueBaixoUseCase.ExecuteAsync(
            new ObterEstoqueBaixoCommand(empresaId, lojaId, limite, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get items expiring soon")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("proximo-vencimento")]
    public async Task<IActionResult> ProximoVencimento(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? dias,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await obterProximoVencimentoUseCase.ExecuteAsync(
            new ObterProximoVencimentoCommand(empresaId, lojaId, dias, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get slow-moving items", Description = "Items without any stock movement in N days.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("parados")]
    public async Task<IActionResult> ItensParados(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? diasSemMovimento,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await obterItensParadosUseCase.ExecuteAsync(
            new ObterItensParadosCommand(empresaId, lojaId, diasSemMovimento, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get AI replenishment suggestions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("sugestao-reposicao")]
    public async Task<IActionResult> SugestaoReposicao(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? limiteQuantidade = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await obterSugestaoReposicaoUseCase.ExecuteAsync(
            new ObterSugestaoReposicaoCommand(empresaId, lojaId, limiteQuantidade, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get product turnover rate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("rotatividade")]
    public async Task<IActionResult> Rotatividade(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? produtoId,
        [FromQuery] int diasHistorico = 30)
    {
        var result = await calcRotatividadeUseCase.ExecuteAsync(
            new CalcularRotatividadeCommand(empresaId, produtoId, diasHistorico));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Get product seasonality")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("sazonalidade")]
    public async Task<IActionResult> Sazonalidade(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid produtoId,
        [FromQuery] int meses = 12)
    {
        var result = await calcSazonalidadeUseCase.ExecuteAsync(
            new CalcularSazonalidadeInteligenciaCommand(empresaId, produtoId, meses));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Get stockout projections")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("projecao-ruptura")]
    public async Task<IActionResult> ProjecaoRuptura(
        [FromQuery] Guid empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await calcProjecaoRupturaUseCase.ExecuteAsync(
            new CalcularProjecaoRupturaCommand(empresaId, page, pageSize));
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get intelligence board summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("board")]
    public async Task<IActionResult> Board([FromQuery] Guid empresaId, [FromQuery] int periodo = 30)
    {
        var result = await getBoardUseCase.ExecuteAsync(
            new GetBoardCommand(empresaId, periodo));
        return DataOk(result);
    }
}
