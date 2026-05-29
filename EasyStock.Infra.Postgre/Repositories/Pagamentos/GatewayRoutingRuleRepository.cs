using EasyStock.Application.Ports.Output.Pagamentos;
using EasyStock.Domain.Entities.Pagamentos;
using EasyStock.Infra.Postgre.Data;
using Microsoft.Extensions.Caching.Memory;

namespace EasyStock.Infra.Postgre.Repositories.Pagamentos;

/// <summary>
/// Implementacao com cache local (TTL 60s). Tipo isento do Global Query
/// Filter (ver <c>EasyStockDbContext.SkipTenantFilter</c>) — repository
/// filtra manualmente "EmpresaId == tenant OR EmpresaId IS NULL" para suportar
/// regras globais.
/// </summary>
public sealed class GatewayRoutingRuleRepository(EasyStockDbContext db, IMemoryCache cache)
    : IGatewayRoutingRuleRepository
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<IReadOnlyList<GatewayRoutingRule>> ObterRegrasAplicaveisAsync(
        Guid empresaId,
        string metodo,
        string moeda = "BRL",
        string pais = "BR",
        CancellationToken ct = default)
    {
        var key = CacheKey(empresaId, metodo, moeda, pais);
        if (cache.TryGetValue<IReadOnlyList<GatewayRoutingRule>>(key, out var cached) && cached is not null)
            return cached;

        var metodoNorm = (metodo ?? "").Trim().ToLowerInvariant();
        var moedaNorm = string.IsNullOrWhiteSpace(moeda) ? "BRL" : moeda.Trim().ToUpperInvariant();
        var paisNorm = string.IsNullOrWhiteSpace(pais) ? "BR" : pais.Trim().ToUpperInvariant();

        var regras = await db.GatewayRoutingRules
            .AsNoTracking()
            .Where(r => r.Ativo
                     && r.Metodo == metodoNorm
                     && r.Moeda == moedaNorm
                     && r.Pais == paisNorm
                     && (r.EmpresaId == empresaId || r.EmpresaId == null))
            .OrderBy(r => r.Prioridade)
            .ThenBy(r => r.Id)
            .ToListAsync(ct);

        cache.Set(key, (IReadOnlyList<GatewayRoutingRule>)regras, CacheTtl);
        return regras;
    }

    public void InvalidarCache(Guid? empresaId)
    {
        // Como cache key inclui empresaId/metodo/moeda/pais e nao temos uma
        // listagem fixa de combinacoes, marcamos um token global de versao.
        // Em P0 simplificamos: o caller e responsavel por chamar admin endpoint
        // que invalida cache. Implementacao futura usa IChangeToken.
        // Por enquanto: no-op (cache expira em 60s).
        _ = empresaId;
    }

    private static string CacheKey(Guid empresaId, string metodo, string moeda, string pais) =>
        $"routing:{empresaId:N}:{(metodo ?? "").ToLowerInvariant()}:{(moeda ?? "BRL").ToUpperInvariant()}:{(pais ?? "BR").ToUpperInvariant()}";
}
