using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Enums.Storefront;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <param name="Linha">ParaServir | PrepararEmCasa; null = não mexe.</param>
/// <param name="NovidadeAte">"aaaa-mm-dd" define, "" tira a novidade, null não mexe (mesma convenção da tag).</param>
/// <param name="SecaoId">id da categoria (M1.3), "" tira da categoria, null não mexe.</param>
public sealed record ItemCardapioRequest(
    string? Nome, LinhaProduto? Linha, string? Porcao, decimal? Preco, string? Categoria,
    string? Descricao = null, string? Ingredientes = null, string? Alergenos = null,
    int? TempoPreparoMinutos = null, string? InstrucaoFinalizacao = null, string? NovidadeAte = null,
    string? SecaoId = null);

public sealed record DefinirVisibilidadeItemRequest(bool Visivel);

public sealed record DefinirArquivadoItemRequest(bool Arquivado);

public sealed record MoverItemRequest(DirecaoMover Direcao);

/// <summary>
/// Itens do cardápio pelo console (#1241, F11). Decisão do Felipe (08/10/2026): incluir, editar e
/// tirar item exigem Gerente; o dia e o saldo ficam com o Operador
/// (<see cref="AtendimentoCardapioDoDiaController"/>). Tirar arquiva (M1.2, D-M1-07), nunca apaga;
/// ocultar só tira do site.
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

    [SwaggerOperation(Summary = "Move a menu item one position up or down (server renumbers 1..n)",
        Description = "direcao: Subir | Descer. Na ponta não muda nada.")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{itemId:guid}/mover")]
    public Task<IActionResult> Mover(Guid itemId, [FromBody] MoverItemRequest req, CancellationToken ct)
        => Tratar(async () => DataOk(await itens.MoverAsync(currentUser.EmpresaId, itemId, req.Direcao, ct)));

    [SwaggerOperation(Summary = "Whole menu item for the edit form (with ingredients and allergens)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpGet("{itemId:guid}")]
    public Task<IActionResult> Obter(Guid itemId, CancellationToken ct)
        => Tratar(async () => DataOk(await itens.ObterAsync(currentUser.EmpresaId, itemId, ct)));

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

    [SwaggerOperation(Summary = "Archive a menu item (take it off the menu) or put it back (idempotent; never deletes)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{itemId:guid}/arquivar")]
    public Task<IActionResult> Arquivar(Guid itemId, [FromBody] DefinirArquivadoItemRequest req, CancellationToken ct)
        => Tratar(async () => DataOk(await itens.DefinirArquivadoAsync(currentUser.EmpresaId, itemId, req.Arquivado, ct)));

    [SwaggerOperation(Summary = "Confirm a new menu item (RN-15): the agent starts offering it")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{itemId:guid}/validar")]
    public Task<IActionResult> Validar(Guid itemId, CancellationToken ct)
        => Tratar(async () =>
        {
            await itens.ConfirmarValidacaoAsync(currentUser.EmpresaId, itemId, ct);
            return NoContent();
        });

    [SwaggerOperation(Summary = "Show or hide a menu item on the site (idempotent)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [HttpPost("{itemId:guid}/visivel")]
    public Task<IActionResult> Visivel(Guid itemId, [FromBody] DefinirVisibilidadeItemRequest req, CancellationToken ct)
        => Tratar(async () => DataOk(await itens.DefinirVisivelAsync(currentUser.EmpresaId, itemId, req.Visivel, ct)));

    private static DadosItemCardapio Dados(ItemCardapioRequest r)
    {
        var mexerNovidade = r.NovidadeAte is not null;
        DateOnly? novidade = null;
        if (!string.IsNullOrEmpty(r.NovidadeAte))
            novidade = DateOnly.TryParse(r.NovidadeAte, System.Globalization.CultureInfo.InvariantCulture, out var data)
                ? data
                : throw new UseCaseValidationException("Data da novidade inválida (use aaaa-mm-dd).");
        var mexerSecao = r.SecaoId is not null;
        Guid? secao = null;
        if (!string.IsNullOrEmpty(r.SecaoId))
            secao = Guid.TryParse(r.SecaoId, out var id) ? id : throw new UseCaseValidationException("Categoria inválida.");
        return new DadosItemCardapio(r.Nome, r.Linha, r.Porcao, r.Preco, r.Categoria,
            r.Descricao, r.Ingredientes, r.Alergenos, r.TempoPreparoMinutos, r.InstrucaoFinalizacao,
            mexerNovidade, novidade, mexerSecao, secao);
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
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }
}
