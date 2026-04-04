using EasyStock.Application.Ports.Output.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/inteligencia")]
public class InteligenciaController : ControllerBase
{
    private readonly IItemEstoqueRepository _itemEstoqueRepository;

    public InteligenciaController(IItemEstoqueRepository itemEstoqueRepository)
    {
        _itemEstoqueRepository = itemEstoqueRepository;
    }

    [HttpGet("estoque-baixo")]
    public async Task<IActionResult> EstoqueBaixo([FromQuery] int limite = 10)
    {
        // Simples: itens com quantidade <= limite
        var itens = await _itemEstoqueRepository.GetAllAsync();
        var baixo = itens.Where(i => i.QuantidadeAtual.Value <= limite);
        return Ok(baixo);
    }

    [HttpGet("proximo-vencimento")]
    public async Task<IActionResult> ProximoVencimento([FromQuery] int dias = 30)
    {
        var itens = await _itemEstoqueRepository.GetAllAsync();
        var proximos = itens.Where(i => i.ValidadeEm != null && i.ValidadeEm.DiasAteVencimento() <= dias);
        return Ok(proximos);
    }

    [HttpGet("parados")]
    public async Task<IActionResult> ItensParados([FromQuery] int diasSemMovimento = 90)
    {
        var itens = await _itemEstoqueRepository.GetAllAsync();
        var parados = itens.Where(i => i.UltimaMovimentacaoEm == null ||
                                       (DateTime.UtcNow - i.UltimaMovimentacaoEm.Value).TotalDays > diasSemMovimento);
        return Ok(parados);
    }

    [HttpGet("sugestao-reposicao")]
    public async Task<IActionResult> SugestaoReposicao()
    {
        // Simples: itens com quantidade baixa
        var itens = await _itemEstoqueRepository.GetAllAsync();
        var sugestoes = itens.Where(i => i.QuantidadeAtual.Value < 5); // Exemplo
        return Ok(sugestoes);
    }
}