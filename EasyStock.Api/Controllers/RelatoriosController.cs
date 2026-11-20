using EasyStock.Application.UseCases.Analytics.PedidosPorAnuncio;
using Microsoft.AspNetCore.RateLimiting;
using Swashbuckle.AspNetCore.Annotations;

namespace EasyStock.Api.Controllers;

/// <summary>Relatórios de leitura do console. O tenant vem sempre do token.</summary>
[SwaggerTag("Relatórios")]
[Authorize]
[ValidateEmpresaId]
[ApiController]
[Route("api/relatorios")]
[EnableRateLimiting("geral")]
public class RelatoriosController(
    ObterPedidosPorAnuncioUseCase obterPedidosPorAnuncioUseCase,
    EasyStock.Application.Ports.Output.ICurrentUserAccessor currentUser) : EasyStockControllerBase
{
    /// <summary>
    /// #2928 (S5): pedidos, valor e ticket médio por plataforma e campanha do anúncio, mais a linha
    /// "Sem origem de anúncio". Pedido válido = não cancelado e não rascunho; o período usa a data de criação
    /// no dia de Brasília (<c>de</c> e <c>ate</c> como <c>YYYY-MM-DD</c>, o último dia incluído; padrão: 30 dias).
    /// </summary>
    [SwaggerOperation(Summary = "Orders by ad platform and campaign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [Authorize(Policy = EasyStock.Api.Authorization.PoliticasPermissao.VisualizarRelatorios)]
    [HttpGet("pedidos-por-anuncio")]
    public async Task<IActionResult> PedidosPorAnuncio([FromQuery] DateTime? de, [FromQuery] DateTime? ate)
    {
        if (!TryResolveEmpresaId(currentUser, null, out var empresaId, out var error))
            return error!;

        var result = await obterPedidosPorAnuncioUseCase.ExecuteAsync(new ObterPedidosPorAnuncioCommand(empresaId, de, ate));
        return DataOk(result);
    }
}
