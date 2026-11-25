using EasyStock.Application.UseCases.GerenciarProduto;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>
/// #3299: o desempenho dos produtos na tela Produtos de Cadastros: vendas de 90 dias, tendência, curva ABC, ritmo,
/// parados e margem, com a faixa da tela. Módulo Cardápio, como a lista de produtos. A rota literal vence o
/// <c>api/produtos/{id}</c>.
/// </summary>
[SwaggerTag("Products / Produtos")]
[ApiController]
[Route("api/produtos/desempenho")]
[Authorize]
[ValidateEmpresaId]
public sealed class ProdutosDesempenhoController(
    DesempenhoDosProdutosUseCase desempenho, ListarProdutosPorDesempenhoUseCase lista, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Product performance (90-day sales, trend, ABC curve, pace, stalled, margin) and the screen summary")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Get([FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        return DataOk(await desempenho.ExecuteAsync(emp, ct));
    }

    /// <summary>
    /// #3303: a lista de Produtos ordenada pelas vendas (<c>receita90</c>, <c>semvender</c>) ou filtrada por um sinal da
    /// faixa, com a mesma entidade e envelope de <c>GET api/produtos</c>. Fica aqui para manter a exigência de Operador.
    /// </summary>
    [SwaggerOperation(Summary = "Products list sorted by sales or filtered by a summary signal (stalled, never sold, below list price, no cost)")]
    [HttpGet("lista")]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Lista(
        [FromQuery] Guid? empresaId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? sort = ListarProdutosPorDesempenhoUseCase.Receita90,
        [FromQuery] string? order = "desc",
        [FromQuery] SinalDoProduto? sinal = null,
        [FromQuery] StatusProduto? status = null,
        [FromQuery] Guid? categoriaId = null,
        CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        var (p, ps) = NormalisePage(page, pageSize);
        try
        {
            var (produtos, total) = await lista.ExecuteAsync(emp, new FiltroPorDesempenho(sort, order, sinal, status, categoriaId), p, ps, ct);
            return DataPaged(produtos, total, p, ps);
        }
        catch (ArgumentException ex) { return DataBadRequest(ex.Message); }
    }
}
