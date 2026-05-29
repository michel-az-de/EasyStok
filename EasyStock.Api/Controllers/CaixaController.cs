using EasyStock.Application.UseCases.AbrirCaixa;
using EasyStock.Application.UseCases.EstornarMovimentoCaixa;
using EasyStock.Application.UseCases.FecharCaixa;
using EasyStock.Application.UseCases.ListarFechamentosCaixa;
using EasyStock.Application.UseCases.ListarMovimentosCaixa;
using EasyStock.Application.UseCases.ObterCaixaDia;
using EasyStock.Application.UseCases.RegistrarMovimentoCaixa;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

[SwaggerTag("Caixa (movimentos + fechamento)")]
[ApiController]
[Route("api/caixa")]
[Authorize]
[ValidateEmpresaId]
public class CaixaController(
    AbrirCaixaUseCase abrirUseCase,
    FecharCaixaUseCase fecharUseCase,
    RegistrarMovimentoCaixaUseCase registrarUseCase,
    EstornarMovimentoCaixaUseCase estornarUseCase,
    ListarMovimentosCaixaUseCase listarMovUseCase,
    ObterCaixaDiaUseCase obterDiaUseCase,
    ListarFechamentosCaixaUseCase listarFechUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Cash summary for a day (open or closed)")]
    [HttpGet("dia")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ObterDia(
        [FromQuery] Guid? empresaId, [FromQuery] DateOnly? data, [FromQuery] Guid? lojaId)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var d = data ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await obterDiaUseCase.ExecuteAsync(new ObterCaixaDiaQuery(emp, d, lojaId));
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "Open cash (with optional initial balance)")]
    [HttpPost("abrir")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Abrir([FromBody] AbrirCaixaCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await abrirUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            RegistradoPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return DataCreated($"/api/caixa/movimentos/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Close cash for a day (snapshot)")]
    [HttpPost("fechar")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> Fechar([FromBody] FecharCaixaCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await fecharUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            FechadoPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null
        });
        return DataOk(result);
    }

    [SwaggerOperation(Summary = "List cash movements (paginated)")]
    [HttpGet("movimentos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ListarMovimentos(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? tipo = null,
        [FromQuery] DateTime? desde = null, [FromQuery] DateTime? ate = null,
        [FromQuery] bool incluirEstornados = false,
        [FromQuery] string? sort = "datamovimento", [FromQuery] string? order = "desc")
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var result = await listarMovUseCase.ExecuteAsync(new ListarMovimentosCaixaQuery(
            emp, p, sz, tipo, desde, ate, incluirEstornados, sort, NormaliseOrder(order)));
        return DataPaged(result.Items, result.Total, result.Page, result.PageSize);
    }

    [SwaggerOperation(Summary = "Register entry/exit (sangria, suprimento, despesa)")]
    [HttpPost("movimentos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> RegistrarMovimento([FromBody] RegistrarMovimentoCaixaCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await registrarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            RegistradoPorUserId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null,
            Origem = command.Origem ?? "web"
        });
        return DataCreated($"/api/caixa/movimentos/{result.Id}", result);
    }

    [SwaggerOperation(Summary = "Reverse a cash movement")]
    [HttpPost("movimentos/{id}/estornar")]
    [Authorize(Policy = "Gerente")]
    public async Task<IActionResult> EstornarMovimento(Guid id, [FromBody] EstornarMovimentoCaixaCommand command)
    {
        if (!TryResolveEmpresaId(currentUser, command.EmpresaId, out var emp, out var err)) return err!;
        var result = await estornarUseCase.ExecuteAsync(command with
        {
            EmpresaId = emp,
            Id = id,
            UsuarioId = currentUser.UsuarioId != Guid.Empty ? currentUser.UsuarioId : null
        });
        return result == null ? DataNotFound("Movimento não encontrado.") : DataOk(result);
    }

    [SwaggerOperation(Summary = "List cash closings (history)")]
    [HttpGet("fechamentos")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> ListarFechamentos(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30,
        [FromQuery] DateOnly? desde = null, [FromQuery] DateOnly? ate = null)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, sz) = NormalisePage(page, pageSize);
        var result = await listarFechUseCase.ExecuteAsync(new ListarFechamentosCaixaQuery(
            emp, p, sz, desde, ate));
        return DataPaged(result.Items, result.Total, result.Page, result.PageSize);
    }
}
