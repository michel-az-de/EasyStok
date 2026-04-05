using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/analytics")]
[EnableRateLimiting("geral")]
public class AnalyticsController(
    IAnalyticsRepository analyticsRepository,
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IOptions<EasyStockConfiguracoes> config) : ControllerBase
{
    private readonly EasyStockConfiguracoes _config = config.Value;

    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard([FromQuery] Guid empresaId, [FromQuery] int periodo = 30)
    {
        var result = await analyticsRepository.GetDashboardResumoAsync(empresaId, periodo);
        return Ok(result);
    }

    [HttpGet("projecoes")]
    public async Task<IActionResult> Projecoes([FromQuery] Guid empresaId, [FromQuery] int diasHistorico = 30, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await analyticsRepository.GetProjecaoRupturaAsync(empresaId, diasHistorico, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("reposicao")]
    public async Task<IActionResult> Reposicao([FromQuery] Guid empresaId, [FromQuery] int diasHistorico = 30, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (items, totalCount) = await analyticsRepository.GetSugestaoReposicaoDetalhadaAsync(empresaId, diasHistorico, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("sazonalidade")]
    public async Task<IActionResult> Sazonalidade([FromQuery] Guid empresaId, [FromQuery] Guid produtoId, [FromQuery] int meses = 12)
    {
        var result = await analyticsRepository.GetSazonalidadeAsync(empresaId, produtoId, meses);
        return Ok(result);
    }

    [HttpGet("alertas")]
    public async Task<IActionResult> Alertas([FromQuery] Guid empresaId, [FromQuery] Guid? lojaId, [FromQuery] int? dias = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = dias ?? await ObterDiasAlertaValidadeAsync(lojaId);
        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(empresaId, diasEfetivos, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("receita")]
    public async Task<IActionResult> Receita([FromQuery] Guid empresaId, [FromQuery] int meses = 12)
    {
        var result = await analyticsRepository.GetReceitaPorPeriodoAsync(empresaId, meses);
        return Ok(result);
    }

    [HttpGet("margem")]
    public async Task<IActionResult> Margem([FromQuery] Guid empresaId, [FromQuery] int dias = 30, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var result = await analyticsRepository.GetMargemPorProdutoAsync(empresaId, dias, page, pageSize);
        return Ok(new { Items = result, Page = page, PageSize = pageSize });
    }

    [HttpGet("movimentacoes")]
    public async Task<IActionResult> Movimentacoes([FromQuery] Guid empresaId, [FromQuery] DateTime? de, [FromQuery] DateTime? ate, [FromQuery] TipoMovimentacaoEstoque? tipo, [FromQuery] int diasPadrao = 30)
    {
        var dataAte = ate ?? DateTime.UtcNow;
        var dataDe = de ?? dataAte.AddDays(-diasPadrao);
        var result = await analyticsRepository.GetMovimentacoesResumoAsync(empresaId, dataDe, dataAte, tipo);
        return Ok(result);
    }

    [HttpGet("validade")]
    public async Task<IActionResult> Validade([FromQuery] Guid empresaId, [FromQuery] Guid? lojaId, [FromQuery] int? dias = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = dias ?? await ObterDiasAlertaValidadeAsync(lojaId);
        var (items, totalCount) = await analyticsRepository.GetAlertasValidadeAsync(empresaId, diasEfetivos, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("parados")]
    public async Task<IActionResult> Parados([FromQuery] Guid empresaId, [FromQuery] Guid? lojaId, [FromQuery] int? diasSemMovimento = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = diasSemMovimento ?? await ObterDiasAlertaParadoAsync(lojaId);
        var (items, totalCount) = await analyticsRepository.GetItensParadosDetalhadosAsync(empresaId, diasEfetivos, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("vendas-por-canal")]
    public async Task<IActionResult> VendasPorCanal([FromQuery] Guid empresaId, [FromQuery] DateTime? de, [FromQuery] DateTime? ate, [FromQuery] int diasPadrao = 30)
    {
        var dataAte = ate ?? DateTime.UtcNow;
        var dataDe = de ?? dataAte.AddDays(-diasPadrao);
        var result = await analyticsRepository.GetVendasPorCanalAsync(empresaId, dataDe, dataAte);
        return Ok(result);
    }

    private async Task<int> ObterDiasAlertaValidadeAsync(Guid? lojaId)
    {
        if (!lojaId.HasValue)
            return _config.DiasAlertaVencimento;

        var configuracao = await configuracaoLojaRepository.GetByLojaIdAsync(lojaId.Value);
        return configuracao?.DiasAlertaValidade ?? _config.DiasAlertaVencimento;
    }

    private async Task<int> ObterDiasAlertaParadoAsync(Guid? lojaId)
    {
        if (!lojaId.HasValue)
            return _config.DiasItemParado;

        var configuracao = await configuracaoLojaRepository.GetByLojaIdAsync(lojaId.Value);
        return configuracao?.DiasAlertaParado ?? _config.DiasItemParado;
    }
}
