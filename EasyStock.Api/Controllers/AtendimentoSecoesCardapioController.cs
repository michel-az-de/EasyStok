using EasyStock.Application.UseCases.Atendimento.Comanda;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record NomeSecaoRequest(string Nome);

/// <summary>
/// Categorias do cardápio pelo console (M1.3, #1483). D-M1-01 (Felipe, 08/10/2026): a categoria que o
/// cliente vê é a seção do cardápio. Gerente, como o resto da autoria do cardápio.
/// </summary>
[SwaggerTag("Attendance order ticket (console)")]
[ApiController]
[Route("api/atendimento/comanda/cardapio/secoes")]
[Authorize(Policy = "Gerente")]
public class AtendimentoSecoesCardapioController(
    SecoesDoCardapioComandaUseCase secoes,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Menu categories (sections) in order, with the number of dishes")]
    [HttpGet]
    public Task<IActionResult> Listar(CancellationToken ct)
        => Tratar(async () => DataOk(await secoes.ListarAsync(currentUser.EmpresaId, ct)));

    [SwaggerOperation(Summary = "Create a menu category at the end of the list")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [HttpPost]
    public Task<IActionResult> Criar([FromBody] NomeSecaoRequest req, CancellationToken ct)
        => Tratar(async () =>
        {
            var s = await secoes.CriarAsync(currentUser.EmpresaId, req.Nome, ct);
            return DataCreated($"/api/atendimento/comanda/cardapio/secoes/{s.SecaoId}", s);
        });

    [SwaggerOperation(Summary = "Rename a menu category")]
    [HttpPut("{secaoId:guid}")]
    public Task<IActionResult> Renomear(Guid secaoId, [FromBody] NomeSecaoRequest req, CancellationToken ct)
        => Tratar(async () =>
        {
            await secoes.RenomearAsync(currentUser.EmpresaId, secaoId, req.Nome, ct);
            return NoContent();
        });

    [SwaggerOperation(Summary = "Show or hide a whole category on the site and the order ticket")]
    [HttpPost("{secaoId:guid}/visivel")]
    public Task<IActionResult> Visivel(Guid secaoId, [FromBody] DefinirVisibilidadeItemRequest req, CancellationToken ct)
        => Tratar(async () =>
        {
            await secoes.DefinirVisivelAsync(currentUser.EmpresaId, secaoId, req.Visivel, ct);
            return NoContent();
        });

    [SwaggerOperation(Summary = "Move a category one position (server renumbers 1..n)")]
    [HttpPost("{secaoId:guid}/mover")]
    public Task<IActionResult> Mover(Guid secaoId, [FromBody] MoverItemRequest req, CancellationToken ct)
        => Tratar(async () =>
        {
            await secoes.MoverAsync(currentUser.EmpresaId, secaoId, req.Direcao, ct);
            return NoContent();
        });

    [SwaggerOperation(Summary = "Delete an empty category (with dishes: 400)")]
    [HttpDelete("{secaoId:guid}")]
    public Task<IActionResult> Excluir(Guid secaoId, CancellationToken ct)
        => Tratar(async () =>
        {
            await secoes.ExcluirAsync(currentUser.EmpresaId, secaoId, ct);
            return NoContent();
        });

    [SwaggerOperation(Summary = "Turn the old free-text categories into sections (idempotent)",
        Description = "Uma seção por texto distinto; liga os pratos sem seção. Não apaga o texto antigo.")]
    [HttpPost("migrar-categorias")]
    public Task<IActionResult> MigrarCategorias(CancellationToken ct)
        => Tratar(async () => DataOk(await secoes.MigrarCategoriasAsync(currentUser.EmpresaId, ct)));

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
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }
}
