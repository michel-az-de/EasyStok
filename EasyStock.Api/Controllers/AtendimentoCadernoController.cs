using EasyStock.Application.UseCases.Atendimento.Caderno;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Caderno da loja (S54): trechos de conhecimento que o agente de atendimento usa. Os do núcleo vão em
/// toda resposta; os demais entram como índice e o agente lê o texto quando precisa.
/// </summary>
[SwaggerTag("Store notebook")]
[ApiController]
[Route("api/atendimento/caderno")]
[Authorize(Policy = "Operador")]
public class AtendimentoCadernoController(
    CadernoUseCases useCases,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List notebook entries", Description = "Arquivados só com arquivados=true.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery] bool arquivados, CancellationToken ct)
        => DataOk(await useCases.ListarAsync(currentUser.EmpresaId, arquivados, ct));

    [SwaggerOperation(Summary = "Create a notebook entry", Description = "Núcleo acima do teto devolve 400.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> Criar([FromBody] TrechoCadernoBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await useCases.CriarAsync(Comando(body), ct)));

    [SwaggerOperation(Summary = "Edit a notebook entry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Editar(Guid id, [FromBody] TrechoCadernoBody body, CancellationToken ct) =>
        Executar(async () => DataOk(await useCases.EditarAsync(id, Comando(body), ct)));

    [SwaggerOperation(Summary = "Archive or restore a notebook entry")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{id:guid}/arquivar")]
    public Task<IActionResult> Arquivar(Guid id, [FromQuery] bool arquivado = true, CancellationToken ct = default) =>
        Executar(async () => DataOk(await useCases.ArquivarAsync(currentUser.EmpresaId, id, arquivado, ct)));

    private SalvarTrechoCadernoCommand Comando(TrechoCadernoBody body) =>
        new(currentUser.EmpresaId, body.Titulo ?? string.Empty, body.Texto ?? string.Empty, body.PalavrasChave, body.Nucleo);

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (TrechoCadernoNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record TrechoCadernoBody(string? Titulo, string? Texto, string? PalavrasChave, bool Nucleo);
