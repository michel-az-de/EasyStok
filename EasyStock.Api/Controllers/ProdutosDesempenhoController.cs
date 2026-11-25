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
public sealed class ProdutosDesempenhoController(DesempenhoDosProdutosUseCase desempenho, ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    [SwaggerOperation(Summary = "Product performance (90-day sales, trend, ABC curve, pace, stalled, margin) and the screen summary")]
    [HttpGet]
    [Authorize(Policy = "Operador")]
    public async Task<IActionResult> Get([FromQuery] Guid? empresaId, CancellationToken ct = default)
    {
        if (!TryResolveEmpresaId(currentUser, empresaId, out var emp, out var err)) return err!;
        return DataOk(await desempenho.ExecuteAsync(emp, ct));
    }
}
