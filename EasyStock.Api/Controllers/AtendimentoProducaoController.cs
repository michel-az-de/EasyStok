using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Produção pelo console (M2, plano docs/plan/erp-casa-da-baba/03-m2-producao.md). D-M2-06 (Felipe,
/// 09/10/2026): quem cozinha lança a produção e vê o estoque do dia; ver e mexer no estoque ainda
/// exige a permissão de estoque, como os desacertos (S22).
/// </summary>
[SwaggerTag("Production (console)")]
[ApiController]
[Route("api/atendimento/producao")]
[Authorize(Policy = "Operador")]
public class AtendimentoProducaoController(
    EstoqueDoDiaUseCase estoqueDoDia,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Today's stock by menu dish: portions, lots (expiring) and sold-without-stock alerts (M2.1)")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpGet("estoque-do-dia")]
    public async Task<IActionResult> EstoqueDoDia(CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            return DataOk(await estoqueDoDia.ExecuteAsync(currentUser.EmpresaId, ct));
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
    }
}
