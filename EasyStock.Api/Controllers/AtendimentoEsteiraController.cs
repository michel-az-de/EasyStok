using EasyStock.Application.UseCases.Atendimento.Esteira;
using EasyStock.Application.UseCases.Common;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Esteira do pedido pelo console (S46, US-042): lançamento em lote do que a dona anotou no papel
/// enquanto a conexão estava fora. Os avisos ao cliente não saem retroativamente.
/// </summary>
[SwaggerTag("Order pipeline")]
[ApiController]
[Route("api/atendimento/esteira")]
[Authorize(Policy = "Operador")]
public class AtendimentoEsteiraController(
    LancarLotePapelUseCase lancarLoteUseCase,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Apply a paper batch of order steps",
        Description = "Aplica na ordem de ocorreuEm, gravando o horário real. Linha inválida volta com o motivo, sem abortar o resto.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost("lote")]
    public async Task<IActionResult> LancarLote([FromBody] LancarLotePapelBody body, CancellationToken ct)
    {
        try
        {
            return DataOk(await lancarLoteUseCase.ExecuteAsync(new LancarLotePapelCommand(
                currentUser.EmpresaId, body.Linhas ?? [], currentUser.UsuarioId, currentUser.Nivel), ct));
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}

public sealed record LancarLotePapelBody(IReadOnlyList<LinhaLotePapel> Linhas);
