using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record DefinirDisponibilidadeItemRequest(bool Disponivel);

public sealed record AjustarSaldoItemRequest(decimal QuantidadeContada, string? Motivo, Guid? LojaId = null);

/// <summary>
/// Cardápio do dia pelo console (#1241, F11). Decisão do Felipe (08/10/2026): o operador do balcão
/// liga e desliga o item hoje e ajusta o saldo contado. Editar o item fica com Gerente. As rotas de
/// <c>api/minha-vitrine</c> exigem Admin e ficam para o painel da vitrine. A empresa vem do token; o
/// item é o <c>cardapio_item_id</c> que a comanda já lê.
/// </summary>
[SwaggerTag("Attendance order ticket (console)")]
[ApiController]
[Route("api/atendimento/comanda/cardapio/{itemId:guid}")]
[Authorize(Policy = "Operador")]
public class AtendimentoCardapioDoDiaController(
    CardapioDoDiaComandaUseCase cardapioDoDia,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Turn a menu item on or off for today (idempotent)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("disponivel")]
    public Task<IActionResult> Disponivel(Guid itemId, [FromBody] DefinirDisponibilidadeItemRequest req, CancellationToken ct)
        => Tratar(async () => DataOk(await cardapioDoDia.DefinirDisponibilidadeAsync(
            new DefinirDisponibilidadeItemInput(currentUser.EmpresaId, itemId, req.Disponivel), ct)));

    [SwaggerOperation(Summary = "Set the counted stock of a menu item (quick adjustment, S22)",
        Description = "O produto sai do item do cardápio. Item avulso (sem produto) não controla saldo: 400.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("saldo")]
    public Task<IActionResult> Saldo(Guid itemId, [FromBody] AjustarSaldoItemRequest req, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Task.FromResult<IActionResult>(Forbid());
        return Tratar(async () => DataOk(await cardapioDoDia.AjustarSaldoAsync(new AjustarSaldoItemInput(
            currentUser.EmpresaId, currentUser.UsuarioId, itemId, req.LojaId, req.QuantidadeContada, req.Motivo ?? string.Empty), ct)));
    }

    private async Task<IActionResult> Tratar(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
        catch (CardapioItemNaoEncontradoException)
        {
            return DataNotFound("Item do cardápio não encontrado.");
        }
        catch (UseCaseValidationException ex)
        {
            return DataBadRequest(ex.Message);
        }
    }
}
