using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <param name="Linha">ParaServir | PrepararEmCasa; null = não mexe.</param>
public sealed record ItemCardapioRequest(string? Nome, LinhaProduto? Linha, string? Porcao, decimal? Preco, string? Categoria);

public sealed record DefinirVisibilidadeItemRequest(bool Visivel);

public sealed record DefinirOrdemItemRequest(double NovaOrdem);

/// <summary>
/// Itens do cardápio pelo console (#1241, F11). Decisão do Felipe (08/10/2026): incluir, editar e
/// tirar item exigem Gerente; o dia e o saldo ficam com o Operador
/// (<see cref="AtendimentoCardapioDoDiaController"/>). Tirar esconde o item, nunca apaga.
/// </summary>
[SwaggerTag("Attendance order ticket (console)")]
[ApiController]
[Route("api/atendimento/comanda/cardapio")]
[Authorize(Policy = "Gerente")]
public class AtendimentoItensCardapioController(
    ItensDoCardapioComandaUseCase itens,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Menu items taken off the menu (hidden), to put back")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("fora")]
    public Task<IActionResult> Fora(CancellationToken ct)
        => Tratar(async () => DataOk(await itens.ListarForaAsync(currentUser.EmpresaId, ct)));

    [SwaggerOperation(Summary = "All menu items for the management screen (M1.1), hidden and off-today included")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [HttpGet("gestao")]
    public Task<IActionResult> Gestao(CancellationToken ct)
        => Tratar(async () => DataOk(await itens.ListarGestaoAsync(currentUser.EmpresaId, ct)));

    [SwaggerOperation(Summary = "Move a menu item (order between neighbours)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{itemId:guid}/ordem")]
    public Task<IActionResult> Ordem(Guid itemId, [FromBody] DefinirOrdemItemRequest req, CancellationToken ct)
        => Tratar(async () => DataOk(await itens.DefinirOrdemAsync(currentUser.EmpresaId, itemId, req.NovaOrdem, ct)));

    [SwaggerOperation(Summary = "Add a standalone menu item (visible)")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [HttpPost]
    public Task<IActionResult> Incluir([FromBody] ItemCardapioRequest req, CancellationToken ct)
        => Tratar(async () =>
        {
            var r = await itens.IncluirAsync(currentUser.EmpresaId, Dados(req), ct);
            return DataCreated($"/api/atendimento/comanda/cardapio/{r.ItemId}", r);
        });

    [SwaggerOperation(Summary = "Edit name, line, portion, price or category of a menu item (null = keep)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPut("{itemId:guid}")]
    public Task<IActionResult> Editar(Guid itemId, [FromBody] ItemCardapioRequest req, CancellationToken ct)
        => Tratar(async () =>
        {
            await itens.EditarAsync(currentUser.EmpresaId, itemId, Dados(req), ct);
            return NoContent();
        });

    [SwaggerOperation(Summary = "Take a menu item off the menu or put it back (idempotent; never deletes)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{itemId:guid}/visivel")]
    public Task<IActionResult> Visivel(Guid itemId, [FromBody] DefinirVisibilidadeItemRequest req, CancellationToken ct)
        => Tratar(async () => DataOk(await itens.DefinirVisivelAsync(currentUser.EmpresaId, itemId, req.Visivel, ct)));

    private static DadosItemCardapio Dados(ItemCardapioRequest r) => new(r.Nome, r.Linha, r.Porcao, r.Preco, r.Categoria);

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
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }
}
