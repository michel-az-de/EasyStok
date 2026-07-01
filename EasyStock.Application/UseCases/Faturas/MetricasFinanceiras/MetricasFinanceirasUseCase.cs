namespace EasyStock.Application.UseCases.Faturas.MetricasFinanceiras;

/// <summary>Comando do dashboard financeiro.</summary>
/// <param name="DiasRetroativo">Janela retroativa em dias para o calculo de "no periodo" (default 30, clampeado [1,365]).</param>
/// <param name="EmpresaId">Filtro opcional por empresa — admin operacional ja injeta sua empresa via controller.</param>
/// <param name="ForcarRefresh">F13 — quando true, ignora cache e recalcula. Default false (TTL 5min).</param>
public sealed record MetricasFinanceirasCommand(
    int DiasRetroativo = 30,
    Guid? EmpresaId = null,
    bool ForcarRefresh = false
);

/// <summary>
/// Snapshot de metricas financeiras do dashboard admin. A computação vive em
/// <see cref="IMetricasFinanceirasQueries"/> (Infra) porque o modo SuperAdmin cross-tenant
/// exige RLS bypass, que a Application não pode abrir (issue 762 — sem isso, sob role de
/// prod sem BYPASSRLS as agregações truncavam silenciosamente). Aqui ficam o clamp da
/// janela e o cache.
///
/// <para>
/// F13 — cache via <see cref="ICacheService"/> com TTL 5 minutos. Chave
/// <c>metricas:{empresaId|null}:{dias}</c>. Sem cache, 6 queries SQL custavam
/// ~200ms em Postgres com indices. Com cache, &lt; 1ms na hit. Invalidacao
/// via <c>ForcarRefresh=true</c> (admin pode disparar pelo dashboard).
/// </para>
/// </summary>
public class MetricasFinanceirasUseCase(
    IMetricasFinanceirasQueries queries,
    ICacheService cache)
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public async Task<MetricasFinanceirasResult> ExecuteAsync(
        MetricasFinanceirasCommand cmd, CancellationToken ct = default)
    {
        var dias = Math.Clamp(cmd.DiasRetroativo, 1, 365);
        var cacheKey = $"metricas:{cmd.EmpresaId?.ToString("N") ?? "all"}:{dias}";

        if (!cmd.ForcarRefresh)
        {
            var cached = await cache.GetAsync<MetricasFinanceirasResult>(cacheKey);
            if (cached is not null) return cached;
        }

        var result = await queries.ComputarAsync(dias, cmd.EmpresaId, ct);
        await cache.SetAsync(cacheKey, result, CacheTtl);
        return result;
    }
}
