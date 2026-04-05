using EasyStock.Api.Configuration;
using EasyStock.Api.Http;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Intelligence / Inteligência")]
[Authorize]
[ApiController]
[Route("api/inteligencia")]
[EnableRateLimiting("ai")]
public class InteligenciaController(
    IItemEstoqueRepository itemEstoqueRepository,
    IMovimentacaoEstoqueRepository movimentacaoRepository,
    IConfiguracaoLojaRepository configuracaoLojaRepository,
    IOptions<EasyStockConfiguracoes> config) : EasyStockControllerBase
{
    private readonly EasyStockConfiguracoes _config = config.Value;

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
        var configuracao = await ObterConfiguracaoAsync(lojaId);
        var limiteEfetivo = limite ?? configuracao?.QuantidadeMinimaPadrao ?? _config.LimiteEstoqueBaixoDefault;
        var (items, totalCount) = await itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, limiteEfetivo, page, pageSize, lojaId);
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
        var configuracao = await ObterConfiguracaoAsync(lojaId);
        var diasEfetivos = dias ?? configuracao?.DiasAlertaValidade ?? _config.DiasAlertaVencimento;
        var (items, totalCount) = await itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, diasEfetivos, page, pageSize, lojaId);
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
        var configuracao = await ObterConfiguracaoAsync(lojaId);
        var diasEfetivos = diasSemMovimento ?? configuracao?.DiasAlertaParado ?? _config.DiasItemParado;
        var (items, totalCount) = await itemEstoqueRepository.GetItensParadosAsync(empresaId, diasEfetivos, page, pageSize, lojaId);
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
        var configuracao = await ObterConfiguracaoAsync(lojaId);
        var limiteEfetivo = limiteQuantidade ?? configuracao?.QuantidadeMinimaPadrao ?? _config.LimiteEstoqueBaixoDefault;
        var (items, totalCount) = await itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, limiteEfetivo, page, pageSize, lojaId);
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
        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-diasHistorico);
        var taxaDiaria = await movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, de, ate);

        return DataOk(new
        {
            empresaId,
            produtoId,
            periodoDias = diasHistorico,
            taxaSaidaDiaria = Math.Round(taxaDiaria, 2),
            taxaSaidaSemanal = Math.Round(taxaDiaria * 7, 2),
            taxaSaidaMensal = Math.Round(taxaDiaria * 30, 2)
        });
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
        var dados = await movimentacaoRepository.GetAgregacaoMensalAsync(empresaId, produtoId, meses);
        return DataOk(dados.Select(d => new { d.Ano, d.Mes, d.TotalSaidas, d.ValorTotal }));
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
        var (itens, totalCount) = await itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, page, pageSize);
        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-30);
        var itensLista = itens.ToList();
        var taxasPorProduto = await movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId, itensLista.Select(i => i.ProdutoId), de, ate);

        var projecoes = itensLista.Select(item =>
        {
            var taxaDiaria = taxasPorProduto.TryGetValue(item.ProdutoId, out var taxa) ? taxa : 0m;
            var diasAteRuptura = taxaDiaria > 0
                ? (int?)Math.Floor(item.QuantidadeAtual.Value / taxaDiaria)
                : null;
            return new
            {
                itemEstoqueId = item.Id,
                produtoId = item.ProdutoId,
                codigoInterno = item.CodigoInterno,
                quantidadeAtual = item.QuantidadeAtual.Value,
                taxaSaidaDiaria = Math.Round(taxaDiaria, 2),
                diasAteRuptura,
                dataEstimadaRuptura = diasAteRuptura.HasValue
                    ? (DateTime?)DateTime.UtcNow.AddDays(diasAteRuptura.Value) : null
            };
        }).OrderBy(p => p.diasAteRuptura ?? int.MaxValue);

        return DataPaged(projecoes, totalCount, page, pageSize);
    }

    [SwaggerOperation(Summary = "Get intelligence board summary")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [HttpGet("board")]
    public async Task<IActionResult> Board([FromQuery] Guid empresaId, [FromQuery] int periodo = 30)
    {
        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-periodo);
        var taxaDiaria = await movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, de, ate);
        var (quantidadeEmEstoque, valorTotalEstoque, ticketMedioSugerido) = await itemEstoqueRepository.GetResumoEstoqueAsync(empresaId);

        return DataOk(new
        {
            empresaId,
            periodo,
            quantidadeEmEstoque,
            valorTotalEstoque = Math.Round(valorTotalEstoque, 2),
            mediaVendasDiaria = Math.Round(taxaDiaria, 2),
            projecaoVendasPeriodo = Math.Round(taxaDiaria * periodo, 0),
            projecaoReceitaPeriodo = Math.Round(taxaDiaria * periodo * ticketMedioSugerido, 2)
        });
    }

    private async Task<EasyStock.Domain.Entities.ConfiguracaoLoja?> ObterConfiguracaoAsync(Guid? lojaId)
    {
        if (!lojaId.HasValue) return null;
        return await configuracaoLojaRepository.GetByLojaIdAsync(lojaId.Value);
    }
}
