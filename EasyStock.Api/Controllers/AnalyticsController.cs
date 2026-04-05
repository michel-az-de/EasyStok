using EasyStock.Api.Configuration;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Analytics")]
[Authorize]
[ApiController]
[Route("api/analytics")]
[EnableRateLimiting("geral")]
public class AnalyticsController(
    IAnalyticsRepository analyticsRepository,
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IOptions<EasyStockConfiguracoes> config) : EasyStockControllerBase
{
    private readonly EasyStockConfiguracoes _config = config.Value;

    [SwaggerOperation(Summary = "Get dashboard summary", Description = "Aggregated KPIs: total products, stock items, low-stock count, expiring items, monthly revenue.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] Guid empresaId, [FromQuery] int periodo = 30)
        => DataOk(await analyticsRepository.GetDashboardResumoAsync(empresaId, periodo));

    [SwaggerOperation(Summary = "Get stock rupture projections (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("projecoes")]
    public async Task<IActionResult> Projecoes(
        [FromQuery] Guid empresaId,
        [FromQuery] int diasHistorico = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await analyticsRepository.GetProjecaoRupturaAsync(empresaId, diasHistorico, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get replenishment suggestions (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("reposicao")]
    public async Task<IActionResult> Reposicao(
        [FromQuery] Guid empresaId,
        [FromQuery] int diasHistorico = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await analyticsRepository.GetSugestaoReposicaoDetalhadaAsync(empresaId, diasHistorico, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get product seasonality analysis")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("sazonalidade")]
    public async Task<IActionResult> Sazonalidade([FromQuery] Guid empresaId, [FromQuery] Guid produtoId, [FromQuery] int meses = 12)
        => DataOk(await analyticsRepository.GetSazonalidadeAsync(empresaId, produtoId, meses));

    [SwaggerOperation(Summary = "Get stock alerts (paginated)", Description = "Returns low-stock and expiry alerts within a time window.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("alertas")]
    public async Task<IActionResult> Alertas(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? dias = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = dias ?? await ObterDiasAlertaValidadeAsync(lojaId);
        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(empresaId, diasEfetivos, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get revenue by month")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("receita")]
    public async Task<IActionResult> Receita([FromQuery] Guid empresaId, [FromQuery] int meses = 12)
        => DataOk(await analyticsRepository.GetReceitaPorPeriodoAsync(empresaId, meses));

    [SwaggerOperation(Summary = "Get product margin analysis (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("margem")]
    public async Task<IActionResult> Margem(
        [FromQuery] Guid empresaId,
        [FromQuery] int dias = 30,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await analyticsRepository.GetMargemPorProdutoAsync(empresaId, dias, page, pageSize);
        return DataPaged(result, result.Count, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get movement analytics")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("movimentacoes")]
    public async Task<IActionResult> Movimentacoes(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] TipoMovimentacaoEstoque? tipo,
        [FromQuery] int diasPadrao = 30)
    {
        var dataAte = ate ?? DateTime.UtcNow;
        var dataDe = de ?? dataAte.AddDays(-diasPadrao);
        return DataOk(await analyticsRepository.GetMovimentacoesResumoAsync(empresaId, dataDe, dataAte, tipo));
    }

    [SwaggerOperation(Summary = "Get items near expiry (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("validade")]
    public async Task<IActionResult> Validade(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? dias = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = dias ?? await ObterDiasAlertaValidadeAsync(lojaId);
        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(empresaId, diasEfetivos, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get slow-moving items (paginated)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("parados")]
    public async Task<IActionResult> Parados(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? lojaId,
        [FromQuery] int? diasSemMovimento = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = diasSemMovimento ?? await ObterDiasAlertaParadoAsync(lojaId);
        var (items, totalCount) = await analyticsRepository.GetItensParadosDetalhadosAsync(empresaId, diasEfetivos, page, pageSize);
        return DataPaged(items, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get sales by channel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("vendas-por-canal")]
    public async Task<IActionResult> VendasPorCanal(
        [FromQuery] Guid empresaId,
        [FromQuery] DateTime? de,
        [FromQuery] DateTime? ate,
        [FromQuery] int diasPadrao = 30)
    {
        var dataAte = ate ?? DateTime.UtcNow;
        var dataDe = de ?? dataAte.AddDays(-diasPadrao);
        return DataOk(await analyticsRepository.GetVendasPorCanalAsync(empresaId, dataDe, dataAte));
    }

    private async Task<int> ObterDiasAlertaValidadeAsync(Guid? lojaId)
    {
        if (!lojaId.HasValue) return _config.DiasAlertaVencimento;
        var configuracao = await configuracaoLojaRepository.GetByLojaIdAsync(lojaId.Value);
        return configuracao?.DiasAlertaValidade ?? _config.DiasAlertaVencimento;
    }

    private async Task<int> ObterDiasAlertaParadoAsync(Guid? lojaId)
    {
        if (!lojaId.HasValue) return _config.DiasItemParado;
        var configuracao = await configuracaoLojaRepository.GetByLojaIdAsync(lojaId.Value);
        return configuracao?.DiasAlertaParado ?? _config.DiasItemParado;
    }
}
