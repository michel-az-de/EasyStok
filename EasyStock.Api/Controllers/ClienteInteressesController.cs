using EasyStock.Application.UseCases.Campanhas.Interesse;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Entities.Campanhas;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Interesse do cliente num item indisponível, registrado pela dona (S31). O agente registra pela
/// ferramenta <c>registrar_interesse</c>. Quando o item volta, o console é avisado; nada vai ao cliente sozinho.
/// A dona fecha o interesse aqui; o pagamento do pedido com o item fecha sozinho (#1228).
/// </summary>
[SwaggerTag("Customer interest in unavailable items")]
[ApiController]
[Route("api/clientes/{clienteId:guid}/interesses")]
[Authorize(Policy = "Admin")]
public class ClienteInteressesController(
    RegistrarInteresseItemUseCase registrarUseCase,
    MarcarInteresseAtendidoUseCase marcarAtendidoUseCase,
    ListarInteressesDoClienteUseCase listarUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "List the customer's interests (Admin only)",
        Description = "Abertos e atendidos, do mais recente para o mais antigo. Só da empresa do token.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet]
    public async Task<IActionResult> Listar(Guid clienteId, CancellationToken ct) =>
        DataOk(await listarUseCase.ExecuteAsync(currentUser.EmpresaId, clienteId, ct));

    [SwaggerOperation(Summary = "Mark an interest as attended (Admin only)",
        Description = "Idempotente: repetir mantém o primeiro AtendidoEm. Interesse de outra empresa ou de outro cliente: 404.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{interesseId:guid}/atendido")]
    public async Task<IActionResult> MarcarAtendido(Guid clienteId, Guid interesseId, CancellationToken ct)
    {
        try
        {
            return DataOk(await marcarAtendidoUseCase.ExecuteAsync(currentUser.EmpresaId, clienteId, interesseId, ct));
        }
        catch (InteresseNaoEncontradoException ex)
        {
            return DataNotFound(ex.Message);
        }
    }

    [SwaggerOperation(Summary = "Register interest in an unavailable item (Admin only)",
        Description = "Com CardapioItemId quando o item está no cardápio; senão Descricao livre.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost]
    public async Task<IActionResult> Post(Guid clienteId, [FromBody] RegistrarInteresseBody body, CancellationToken ct)
    {
        try
        {
            var result = await registrarUseCase.ExecuteAsync(new RegistrarInteresseItemCommand(
                currentUser.EmpresaId, clienteId, body.CardapioItemId, body.Descricao, OrigemInteresse.Dona), ct);
            return DataCreated($"/api/clientes/{clienteId}/interesses/{result.Id}", result);
        }
        catch (ClienteNaoEncontradoParaInteresseException ex)
        {
            return DataNotFound(ex.Message);
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record RegistrarInteresseBody(Guid? CardapioItemId, string? Descricao);
