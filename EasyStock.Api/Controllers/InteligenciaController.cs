using EasyStock.Api.Configuration;
using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/inteligencia")]
[EnableRateLimiting("ai")]
public class InteligenciaController : ControllerBase
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository;
    private readonly IMovimentacaoEstoqueRepository _movimentacaoRepository;
    private readonly EasyStockConfiguracoes _config;

    public InteligenciaController(
        IItemEstoqueRepository itemEstoqueRepository,
        IMovimentacaoEstoqueRepository movimentacaoRepository,
        IOptions<EasyStockConfiguracoes> config)
    {
        _itemEstoqueRepository = itemEstoqueRepository;
        _movimentacaoRepository = movimentacaoRepository;
        _config = config.Value;
    }

    [HttpGet("estoque-baixo")]
    public async Task<IActionResult> EstoqueBaixo([FromQuery] Guid empresaId, [FromQuery] int? limite, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var limiteEfetivo = limite ?? _config.LimiteEstoqueBaixoDefault;
        var (items, totalCount) = await _itemEstoqueRepository.GetEstoqueBaixoAsync(empresaId, limiteEfetivo, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("proximo-vencimento")]
    public async Task<IActionResult> ProximoVencimento([FromQuery] Guid empresaId, [FromQuery] int? dias, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = dias ?? _config.DiasAlertaVencimento;
        var (items, totalCount) = await _itemEstoqueRepository.GetProximoVencimentoAsync(empresaId, diasEfetivos, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("parados")]
    public async Task<IActionResult> ItensParados([FromQuery] Guid empresaId, [FromQuery] int? diasSemMovimento, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var diasEfetivos = diasSemMovimento ?? _config.DiasItemParado;
        var (items, totalCount) = await _itemEstoqueRepository.GetItensParadosAsync(empresaId, diasEfetivos, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("sugestao-reposicao")]
    public async Task<IActionResult> SugestaoReposicao([FromQuery] Guid empresaId, [FromQuery] int? limiteQuantidade = null, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var limiteEfetivo = limiteQuantidade ?? _config.LimiteEstoqueBaixoDefault;
        var (items, totalCount) = await _itemEstoqueRepository.GetSugestaoReposicaoAsync(empresaId, limiteEfetivo, page, pageSize);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    [HttpGet("rotatividade")]
    public async Task<IActionResult> Rotatividade(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid? produtoId,
        [FromQuery] int diasHistorico = 30)
    {
        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-diasHistorico);
        var taxaDiaria = await _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, produtoId, de, ate);

        return Ok(new
        {
            EmpresaId = empresaId,
            ProdutoId = produtoId,
            PeriodoDias = diasHistorico,
            TaxaSaidaDiaria = Math.Round(taxaDiaria, 2),
            TaxaSaidaSemanal = Math.Round(taxaDiaria * 7, 2),
            TaxaSaidaMensal = Math.Round(taxaDiaria * 30, 2)
        });
    }

    [HttpGet("sazonalidade")]
    public async Task<IActionResult> Sazonalidade(
        [FromQuery] Guid empresaId,
        [FromQuery] Guid produtoId,
        [FromQuery] int meses = 12)
    {
        var dados = await _movimentacaoRepository.GetAgregacaoMensalAsync(empresaId, produtoId, meses);
        return Ok(dados.Select(d => new
        {
            Ano = d.Ano,
            Mes = d.Mes,
            TotalSaidas = d.TotalSaidas,
            ValorTotal = d.ValorTotal
        }));
    }

    [HttpGet("projecao-ruptura")]
    public async Task<IActionResult> ProjecaoRuptura([FromQuery] Guid empresaId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var (itens, totalCount) = await _itemEstoqueRepository.GetItensEstoquePaginadosAsync(empresaId, page, pageSize);
        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-30);
        var itensLista = itens.ToList();
        var taxasPorProduto = await _movimentacaoRepository.GetTaxaSaidaDiariaPorProdutoAsync(
            empresaId,
            itensLista.Select(i => i.ProdutoId),
            de,
            ate);

        var projecoes = new List<object>();
        foreach (var item in itensLista)
        {
            var taxaDiaria = taxasPorProduto.TryGetValue(item.ProdutoId, out var taxa) ? taxa : 0m;
            var diasAteRuptura = taxaDiaria > 0
                ? (int)Math.Floor(item.QuantidadeAtual.Value / taxaDiaria)
                : (int?)null;

            projecoes.Add(new
            {
                ItemEstoqueId = item.Id,
                ProdutoId = item.ProdutoId,
                CodigoInterno = item.CodigoInterno,
                QuantidadeAtual = item.QuantidadeAtual.Value,
                TaxaSaidaDiaria = Math.Round(taxaDiaria, 2),
                DiasAteRuptura = diasAteRuptura,
                DataEstimadaRuptura = diasAteRuptura.HasValue
                    ? (DateTime?)DateTime.UtcNow.AddDays(diasAteRuptura.Value)
                    : null
            });
        }

        return Ok(new
        {
            Items = projecoes.OrderBy(p => ((dynamic)p).DiasAteRuptura ?? int.MaxValue),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("board")]
    public async Task<IActionResult> Board([FromQuery] Guid empresaId, [FromQuery] int periodo = 30)
    {
        var ate = DateTime.UtcNow;
        var de = ate.AddDays(-periodo);

        var taxaDiaria = await _movimentacaoRepository.GetTaxaSaidaDiariaAsync(empresaId, null, de, ate);
        var (quantidadeEmEstoque, valorTotalEstoque, ticketMedioSugerido) = await _itemEstoqueRepository.GetResumoEstoqueAsync(empresaId);

        return Ok(new
        {
            EmpresaId = empresaId,
            Periodo = periodo,
            QuantidadeEmEstoque = quantidadeEmEstoque,
            ValorTotalEstoque = Math.Round(valorTotalEstoque, 2),
            MediaVendasDiaria = Math.Round(taxaDiaria, 2),
            ProjecaoVendasPeriodo = Math.Round(taxaDiaria * periodo, 0),
            ProjecaoReceitaPeriodo = Math.Round(taxaDiaria * periodo * ticketMedioSugerido, 2)
        });
    }
}
