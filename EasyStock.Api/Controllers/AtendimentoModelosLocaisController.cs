using EasyStock.Application.UseCases.Atendimento.Automacoes;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Mensagens padrão locais (#3204): modelos de mensagem do WhatsApp guardados no EasyStok, no formato que a Meta aprova.
/// Criar, editar e arquivar exigem a policy Gerente e <see cref="Permissao.AtenderConversas"/>, como as respostas prontas;
/// listar basta a policy Operador.
/// </summary>
[SwaggerTag("Local message templates")]
[ApiController]
[Route("api/atendimento/modelos-locais")]
[Authorize(Policy = "Operador")]
public class AtendimentoModelosLocaisController(
    ModelosMensagemLocaisUseCases useCases,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List local message templates", Description = "Arquivadas só com arquivados=true.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar([FromQuery(Name = "arquivados")] bool arquivados, CancellationToken ct)
        => DataOk(await useCases.ListarAsync(currentUser.EmpresaId, arquivados, ct));

    [SwaggerOperation(Summary = "Create a local message template", Description = "Nome e idioma duplicados devolvem 409.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = "Gerente")]
    [HttpPost]
    public Task<IActionResult> Criar([FromBody] ModeloMensagemLocalBody body, CancellationToken ct) =>
        Atendendo(async () => DataOk(await useCases.CriarAsync(Comando(body), ct)));

    [SwaggerOperation(Summary = "Edit a local message template", Description = "Nome e idioma de outra mensagem devolvem 409.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [Authorize(Policy = "Gerente")]
    [HttpPut("{id:guid}")]
    public Task<IActionResult> Editar(Guid id, [FromBody] ModeloMensagemLocalBody body, CancellationToken ct) =>
        Atendendo(async () => DataOk(await useCases.EditarAsync(id, Comando(body), ct)));

    [SwaggerOperation(Summary = "Archive or restore a local message template")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [Authorize(Policy = "Gerente")]
    [HttpPost("{id:guid}/arquivar")]
    public Task<IActionResult> Arquivar(Guid id, [FromQuery] bool arquivado = true, CancellationToken ct = default) =>
        Atendendo(async () => DataOk(await useCases.ArquivarAsync(currentUser.EmpresaId, id, arquivado, ct)));

    private SalvarModeloMensagemLocalCommand Comando(ModeloMensagemLocalBody body) =>
        new(currentUser.EmpresaId, body.Nome ?? string.Empty, body.Idioma ?? string.Empty,
            body.Categoria ?? string.Empty, body.Cabecalho, body.Corpo ?? string.Empty, body.Rodape);

    /// <summary>Escrita é ação de atendimento (#1781), como nas respostas prontas.</summary>
    private Task<IActionResult> Atendendo(Func<Task<IActionResult>> acao)
        => currentUser.TemPermissao(Permissao.AtenderConversas) ? Executar(acao) : Task.FromResult<IActionResult>(Forbid());

    private async Task<IActionResult> Executar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (ModeloMensagemLocalDuplicadoException ex)
        {
            return DataConflict(ex.Message);
        }
        catch (ModeloMensagemLocalNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex);
        }
    }
}

public sealed record ModeloMensagemLocalBody(
    string? Nome, string? Idioma, string? Categoria, string? Cabecalho, string? Corpo, string? Rodape);
