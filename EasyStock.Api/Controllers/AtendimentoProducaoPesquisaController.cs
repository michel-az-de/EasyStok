using EasyStock.Application.Ports.Output.Ai;
using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.Common;
using Microsoft.AspNetCore.RateLimiting;

namespace EasyStock.Api.Controllers;

public sealed record PesquisaReceitaRequest(string Nome);

[ApiController]
[Route("api/atendimento/producao")]
[Authorize(Policy = "Gerente")]
[EnableRateLimiting("ai")]
public class AtendimentoProducaoPesquisaController(IPesquisaProducaoWeb pesquisa, SugerirReceitaUseCase sugestao, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [HttpGet("pesquisa")]
    public async Task<IActionResult> Pesquisar([FromQuery] string texto, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try { return DataOk(await pesquisa.BuscarInsumosAsync(SugerirReceitaUseCase.ValidarTermo(texto), ct)); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return StatusCode(503, new { error = new { message = "A pesquisa pública está indisponível. Continue o cadastro manualmente." } }); }
    }

    [HttpPost("sugerir-receita")]
    public async Task<IActionResult> Sugerir([FromBody] PesquisaReceitaRequest request, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try { return DataOk(await sugestao.ExecuteAsync(request.Nome, ct)); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return StatusCode(503, new { error = new { message = "A IA ou a pesquisa pública está indisponível. Sua receita foi preservada." } }); }
    }
}
