using EasyStock.Application.UseCases.ClienteCrm;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// CRM leve do cliente (S24): tags, notas internas, bloqueio e preferências. Controller próprio para
/// não inchar <see cref="ClientesController"/>; mesmas rotas-base e policies.
/// </summary>
[SwaggerTag("Customer CRM (tags, internal notes, block, preferences)")]
[ApiController]
[Route("api/clientes/{id:guid}")]
[Authorize]
[ValidateEmpresaId]
public class ClientesCrmController(
    ListarTagsClienteUseCase listarTags,
    AdicionarTagClienteUseCase adicionarTag,
    RemoverTagClienteUseCase removerTag,
    ListarNotasClienteUseCase listarNotas,
    AdicionarNotaClienteUseCase adicionarNota,
    DefinirBloqueioClienteUseCase bloqueio,
    DefinirPreferenciasClienteUseCase preferencias,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    // ── Tags ───────────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List client tags (with suggested tags)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("tags")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> ListarTags(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await listarTags.ExecuteAsync(emp, id, ct)));

    [SwaggerOperation(Summary = "Add tag to client", Description = "Normalized (lowercase, no accents, max 40). Duplicate → 409.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [HttpPost("tags")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> AdicionarTag(Guid id, [FromBody] AdicionarTagBody body, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp =>
        {
            var tag = await adicionarTag.ExecuteAsync(new AdicionarTagClienteCommand(emp, id, body.Tag ?? string.Empty), ct);
            return DataCreated($"/api/clientes/{id}/tags/{tag.Tag}", tag);
        });

    [SwaggerOperation(Summary = "Remove tag from client")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpDelete("tags/{tag}")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> RemoverTag(Guid id, string tag, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp =>
            await removerTag.ExecuteAsync(new RemoverTagClienteCommand(emp, id, tag), ct)
                ? NoContent()
                : DataNotFound("O cliente não tem esta tag."));

    // ── Notas internas ─────────────────────────────────────────────────

    [SwaggerOperation(Summary = "List internal notes (newest first)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("notas")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> ListarNotas(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await listarNotas.ExecuteAsync(emp, id, ct)));

    [SwaggerOperation(Summary = "Add internal note", Description = "PedidoId of another client → 400.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("notas")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> AdicionarNota(Guid id, [FromBody] AdicionarNotaBody body, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp =>
        {
            var nota = await adicionarNota.ExecuteAsync(new AdicionarNotaClienteCommand(
                emp, id, body.Texto ?? string.Empty, Autor(), body.PedidoId, body.MensagemId), ct);
            return DataCreated($"/api/clientes/{id}/notas/{nota.Id}", nota);
        });

    // ── Bloqueio ───────────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Block client in every channel")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("bloquear")]
    [Authorize(Policy = "Gerente")]
    public Task<IActionResult> Bloquear(Guid id, [FromBody] BloquearBody? body, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await bloqueio.BloquearAsync(emp, id, body?.Motivo, ct)));

    [SwaggerOperation(Summary = "Unblock client")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("desbloquear")]
    [Authorize(Policy = "Gerente")]
    public Task<IActionResult> Desbloquear(Guid id, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await bloqueio.DesbloquearAsync(emp, id, ct)));

    // ── Preferências ───────────────────────────────────────────────────

    [SwaggerOperation(Summary = "Set client preferences", Description = "Null field = unchanged.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("preferencias")]
    [Authorize(Policy = "Operador")]
    public Task<IActionResult> DefinirPreferencias(Guid id, [FromBody] PreferenciasBody body, [FromQuery] Guid? empresaId, CancellationToken ct) =>
        Executar(empresaId, async emp => DataOk(await preferencias.ExecuteAsync(
            new DefinirPreferenciasClienteCommand(emp, id, body.AvisosStatusAtivos, body.ConsentiuMarketing), ct)));

    /// <summary>Resolve a empresa e traduz as exceções do CRM em 400/404/409.</summary>
    private async Task<IActionResult> Executar(Guid? empresaId, Func<Guid, Task<IActionResult>> acao)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        try
        {
            return await acao(emp);
        }
        catch (ClienteCrmNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (ClienteTagDuplicadaException ex)
        {
            return DataConflict(ex.Message);
        }
        catch (NotaPedidoDeOutroClienteException ex)
        {
            return DataBadRequest(ex.Message);
        }
        catch (RegraDeDominioVioladaException ex)
        {
            // Tag vazia ou longa, nota vazia ou longa, motivo longo: entrada inválida.
            return DataBadRequest(ex.Message);
        }
    }

    private string Autor() =>
        !string.IsNullOrWhiteSpace(User.Identity?.Name)
            ? User.Identity!.Name!
            : $"console:{currentUser.UsuarioId:N}";
}

public sealed record AdicionarTagBody(string? Tag);

public sealed record AdicionarNotaBody(string? Texto, Guid? PedidoId = null, Guid? MensagemId = null);

public sealed record BloquearBody(string? Motivo);

public sealed record PreferenciasBody(bool? AvisosStatusAtivos, bool? ConsentiuMarketing);
