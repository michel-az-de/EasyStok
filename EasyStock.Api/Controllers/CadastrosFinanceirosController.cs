using EasyStock.Application.UseCases.Financeiro.Cadastros;
using EasyStock.Application.UseCases.Common;

namespace EasyStock.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/cadastros")]
public sealed class CadastrosFinanceirosController(CadastrosFinanceirosUseCase cadastros) : EasyStockControllerBase
{
    // O caso de uso resolve o tenant somente da sessão e devolve 400 para corpo divergente.
    [HttpGet("contas-bancarias")]
    public Task<IActionResult> ListarContas(CancellationToken ct) => Executar(async () => DataOk(await cadastros.ListarContasAsync(ct)));

    [HttpGet("contas-bancarias/{id:guid}")]
    public Task<IActionResult> ObterConta(Guid id, CancellationToken ct)
        => Executar(async () => await cadastros.ObterContaAsync(id, ct) is { } conta ? DataOk(conta) : DataNotFound());

    [HttpPost("contas-bancarias")]
    public Task<IActionResult> CriarConta([FromBody] SalvarContaBancariaInput input, CancellationToken ct)
        => Executar(async () => DataOk(await cadastros.SalvarContaAsync(null, input, ct)));

    [HttpPatch("contas-bancarias/{id:guid}")]
    public Task<IActionResult> AtualizarConta(Guid id, [FromBody] SalvarContaBancariaInput input, CancellationToken ct)
        => Executar(async () => await cadastros.SalvarContaAsync(id, input, ct) is { } conta ? DataOk(conta) : DataNotFound());

    [HttpGet("metodos-pagamento")]
    public Task<IActionResult> ListarMetodos(CancellationToken ct) => Executar(async () => DataOk(await cadastros.ListarMetodosAsync(ct)));

    [HttpPut("metodos-pagamento/{codigo}")]
    public Task<IActionResult> ConfigurarMetodo(string codigo, [FromBody] ConfigurarMetodoPagamentoInput input, CancellationToken ct)
        => Executar(async () => DataOk(await cadastros.ConfigurarMetodoAsync(codigo, input, ct)));

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }
}
