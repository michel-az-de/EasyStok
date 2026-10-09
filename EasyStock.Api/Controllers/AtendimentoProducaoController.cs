using EasyStock.Application.UseCases.Atendimento.Producao;
using EasyStock.Application.UseCases.Common;
using EasyStock.Domain.Exceptions.Storefront;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

public sealed record ProduzirPratosRequest(IReadOnlyList<PratoProduzidoInput>? Pratos, string? Observacao = null);

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
    ProduzirPratosUseCase produzirPratos,
    ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Register today's production by menu dish: lot, labels and stock entry in portions (M2.2)",
        Description = "Envie Idempotency-Key: repetir com a mesma chave não cria lote novo. Prato avulso ganha produto de estoque.")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [HttpPost]
    public async Task<IActionResult> Produzir([FromBody] ProduzirPratosRequest req, CancellationToken ct)
    {
        if (!currentUser.TemPermissao(Permissao.GerenciarEstoque)) return Forbid();
        try
        {
            var r = await produzirPratos.ExecuteAsync(new ProduzirPratosCommand(
                currentUser.EmpresaId, currentUser.UsuarioId, null, req.Pratos, req.Observacao), ct);
            return DataCreated($"/api/lotes/{r.LoteId}", r);
        }
        catch (StorefrontNaoEncontradoException)
        {
            return DataNotFound("A empresa não tem vitrine ativa.");
        }
        catch (CardapioItemNaoEncontradoException)
        {
            return DataNotFound("Prato do cardápio não encontrado.");
        }
        catch (Exception ex) when (ex is UseCaseValidationException or RegraDeDominioVioladaException)
        {
            return DataBadRequest(ex.Message);
        }
    }

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
