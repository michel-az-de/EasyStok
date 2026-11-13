using EasyStock.Application.UseCases.Atendimento.ClienteDaConversa;
using EasyStock.Application.UseCases.Atendimento.Inbox;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;

namespace EasyStock.Api.Controllers;

[ApiController]
[Route("api/atendimento/conversas/{id:guid}")]
[Authorize(Policy = "Operador")]
public sealed class AtendimentoVinculoClienteController(VinculoClienteConversaUseCase useCase,
    ICurrentUserAccessor usuario) : EasyStockControllerBase
{
    [HttpGet("vinculo-cliente")]
    public Task<IActionResult> Obter(Guid id, CancellationToken ct) => Tratar(async () =>
        DataOk(await useCase.ObterAsync(usuario.EmpresaId, id, ct)));

    [HttpGet("candidatos-cliente")]
    public Task<IActionResult> Candidatos(Guid id, [FromQuery] string telefone, CancellationToken ct) => Tratar(async () =>
        DataOk(await useCase.CandidatosAsync(usuario.EmpresaId, id, telefone, ct)));

    [HttpPost("vinculo-cliente")]
    public Task<IActionResult> Corrigir(Guid id, [FromBody] CorrigirVinculoClienteBody body, CancellationToken ct) => Tratar(async () =>
    {
        if (body.EmpresaId is { } empresa && empresa != usuario.EmpresaId) return DataBadRequest("Empresa diferente da sessão.");
        return DataOk(await useCase.ExecuteAsync(new(usuario.EmpresaId, id, body.RequestId, body.VinculoRevisao,
            body.ClienteId, body.Acao, body.DestinoClienteId, body.Motivo), ct));
    });

    private async Task<IActionResult> Tratar(Func<Task<IActionResult>> acao)
    {
        try { return await acao(); }
        catch (UnauthorizedAccessException) { return Forbid(); }
        catch (ConversaNaoEncontradaException ex) { return DataNotFound(ex.Message); }
        catch (UseCaseValidationException ex) { return DataBadRequest(ex); }
        catch (TelefoneInvalidoException ex) { return DataBadRequest(ex.Message); }
        catch (Exception ex) when (ex is VinculoClienteConflitoException or RegraDeDominioVioladaException or DbUpdateConcurrencyException)
        {
            return Conflict(new ApiErrorResponse(new ApiError("VINCULO_CLIENTE_CONFLITO",
                ex is DbUpdateConcurrencyException ? "A identidade mudou. Atualize a conversa e confira novamente." : ex.Message, null, null)));
        }
    }
}

public sealed record CorrigirVinculoClienteBody(Guid RequestId, long VinculoRevisao, Guid? ClienteId,
    string Acao, Guid? DestinoClienteId, string Motivo, Guid? EmpresaId = null);
