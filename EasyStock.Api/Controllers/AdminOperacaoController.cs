using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Api.Controllers;

/// <summary>
/// Endpoint do Centro de Comando da Frota (issue 623) — rollup operacional
/// cross-tenant das lojas ativas. Espelha <see cref="AdminDashboardController"/>:
/// SuperAdmin, sem EF cru no controller, delega a <see cref="IFleetOperationQueries"/>.
/// Cache curto em memoria (o painel faz poll a cada ~25s; varias abas de admin nao
/// devem multiplicar a carga no banco).
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "SuperAdmin")]
public class AdminOperacaoController(IFleetOperationQueries fleet, IMemoryCache cache) : EasyStockControllerBase
{
    private const int MaxLinhas = 120;
    private const string CacheKey = "admin-operacao-fleet";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(12);

    [HttpGet("operacao/fleet")]
    public async Task<IActionResult> GetFleet(CancellationToken ct = default)
    {
        var data = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await fleet.ObterAsync(DateTime.UtcNow, MaxLinhas, ct);
        });
        return DataOk(data);
    }
}
