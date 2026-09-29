using EasyStock.Application.UseCases.Atendimento.Consentimento;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Atendimento;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Consentimento do cliente final por canal e finalidade (S38, ADR-0051): o que o console pode
/// mandar por iniciativa própria (mensagem programada, campanha). "SAIR" do cliente revoga pelo webhook.
/// </summary>
[SwaggerTag("Customer contact consent per channel")]
[ApiController]
[Route("api/atendimento/clientes/{clienteId:guid}/consentimentos")]
[Authorize(Policy = "Admin")]
public class AtendimentoConsentimentosController(
    ListarConsentimentosClienteUseCase listarUseCase,
    DefinirConsentimentosClienteUseCase definirUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List consent per channel and purpose (Admin only)",
        Description = "Uma linha por canal e finalidade; Situacao nula = nunca registrado. PodeEnviar já aplica a regra.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet]
    public async Task<IActionResult> Get(Guid clienteId, CancellationToken ct)
    {
        try
        {
            return DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, clienteId, ct));
        }
        catch (ClienteNaoEncontradoParaConsentimentoException ex)
        {
            return DataNotFound(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Grant or revoke consent (Admin only)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut]
    public async Task<IActionResult> Put(Guid clienteId, [FromBody] DefinirConsentimentosBody body, CancellationToken ct)
    {
        try
        {
            var itens = (body.Itens ?? []).Select(i => new ConsentimentoItem(i.Canal, i.Finalidade, i.Situacao)).ToList();
            return DataOk(await definirUseCase.ExecuteAsync(
                new DefinirConsentimentosClienteCommand(currentUser.EmpresaId, clienteId, currentUser.UsuarioId, itens), ct));
        }
        catch (ClienteNaoEncontradoParaConsentimentoException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record ConsentimentoBody(CanalConversa Canal, FinalidadeContato Finalidade, SituacaoConsentimento Situacao);

public sealed record DefinirConsentimentosBody(IReadOnlyList<ConsentimentoBody>? Itens);
