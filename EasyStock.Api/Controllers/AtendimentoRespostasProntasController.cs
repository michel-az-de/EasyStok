using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Biblioteca de respostas prontas do console (S42, US-009): cadastro com atalho único e render das
/// variáveis <c>{nome}</c>, <c>{pedido}</c> e <c>{faixa}</c> no servidor, para uma conversa.
/// </summary>
[SwaggerTag("Canned replies")]
[ApiController]
[Route("api/atendimento/respostas-prontas")]
[Authorize(Policy = "Operador")]
public class AtendimentoRespostasProntasController(
    RespostasProntasUseCases useCases,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List canned replies", Description = "Arquivadas só com arquivadas=true.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool arquivadas, CancellationToken ct)
        => DataOk(await useCases.ListarAsync(currentUser.EmpresaId, arquivadas, ct));

    [SwaggerOperation(Summary = "Create a canned reply", Description = "Atalho duplicado devolve 409.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost]
    public Task<IActionResult> Criar([FromBody] RespostaProntaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await useCases.CriarAsync(Comando(body), ct)));

    [SwaggerOperation(Summary = "Edit a canned reply", Description = "Atalho de outra resposta devolve 409.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Editar(Guid id, [FromBody] RespostaProntaBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await useCases.EditarAsync(id, Comando(body), ct)));

    [SwaggerOperation(Summary = "Archive or restore a canned reply")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/arquivar")]
    public Task<IActionResult> Arquivar(Guid id, [FromQuery] bool arquivada = true, CancellationToken ct = default) =>
        Executar(async () => DataOk(await useCases.ArquivarAsync(currentUser.EmpresaId, id, arquivada, ct)));

    [SwaggerOperation(Summary = "Render a canned reply for a conversation",
        Description = "Variável sem valor na conversa (ex.: {pedido} sem pedido) devolve 400.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{id:guid}/render")]
    public Task<IActionResult> Renderizar(Guid id, [FromQuery] Guid conversaId, CancellationToken ct) =>
        Executar(async () => DataOk(await useCases.RenderizarAsync(currentUser.EmpresaId, id, conversaId, ct)));

    private SalvarRespostaProntaCommand Comando(RespostaProntaBody body) =>
        new(currentUser.EmpresaId, body.Titulo ?? string.Empty, body.Atalho ?? string.Empty, body.Texto ?? string.Empty);

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (AtalhoDuplicadoException ex)
        {
            return DataConflict(ex.Message);
        }
        catch (RespostaProntaNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (ConversaNaoEncontradaException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record RespostaProntaBody(string? Titulo, string? Atalho, string? Texto);
