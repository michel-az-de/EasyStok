using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class OrigemAnuncioRepository(EasyStockDbContext db) : IOrigemAnuncioRepository
{
    public async Task<int> PodarIdsDeCliqueAsync(DateTimeOffset capturadaAntesDe, CancellationToken ct = default)
    {
        // Cross-tenant: roda do job de manutenção, sem requisição e sem tenant.
        using var _ = db.UseRowLevelSecurityBypass();
        var limite = capturadaAntesDe.ToUniversalTime();

        var pedidos = await db.Pedidos
            .IgnoreQueryFilters()
            .Where(p => p.OrigemAnuncio != null && p.OrigemAnuncio.CapturadaEm < limite
                && (p.OrigemAnuncio.IdClique != null || p.OrigemAnuncio.TipoIdClique != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.OrigemAnuncio!.IdClique, (string?)null)
                .SetProperty(p => p.OrigemAnuncio!.TipoIdClique, (string?)null), ct);

        var conversas = await db.AtendimentoConversas
            .IgnoreQueryFilters()
            .Where(c => c.OrigemAnuncio != null && c.OrigemAnuncio.CapturadaEm < limite
                && (c.OrigemAnuncio.IdClique != null || c.OrigemAnuncio.TipoIdClique != null))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.OrigemAnuncio!.IdClique, (string?)null)
                .SetProperty(c => c.OrigemAnuncio!.TipoIdClique, (string?)null), ct);

        return pedidos + conversas;
    }
}
