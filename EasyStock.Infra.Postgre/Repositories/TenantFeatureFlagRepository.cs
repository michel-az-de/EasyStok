using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories;

/// <summary>
/// Feature flags por tenant (ADR-0048).
///
/// <para>
/// ⚠️ Este tipo é isento do Global Query Filter (<c>EasyStockDbContext.SkipTenantFilter</c>)
/// <b>e</b> das políticas de RLS do Postgres (migration <c>AddRowLevelSecurity</c>). As duas
/// redes de segurança que normalmente pegariam um filtro esquecido estão desligadas aqui,
/// então <b>toda</b> query deste repository compara <c>EmpresaId</c> explicitamente. Um
/// esquecimento não vira dado a mais na tela: vira flag de outro tenant.
/// </para>
/// </summary>
public sealed class TenantFeatureFlagRepository(EasyStockDbContext db) : ITenantFeatureFlagRepository
{
    public async Task<IReadOnlyList<TenantFeatureFlagItem>> ListarPorEmpresaAsync(
        Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty) return [];

        return await db.TenantFeatureFlags
            .AsNoTracking()
            .Where(f => f.EmpresaId == empresaId)
            .OrderBy(f => f.Feature)
            .Select(f => new TenantFeatureFlagItem(f.Feature, f.Ativo, f.AlteradoEm, f.AlteradoPor))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<string>> ListarAtivasAsync(Guid empresaId, CancellationToken ct = default)
    {
        if (empresaId == Guid.Empty) return [];

        return await db.TenantFeatureFlags
            .AsNoTracking()
            .Where(f => f.EmpresaId == empresaId && f.Ativo)
            .OrderBy(f => f.Feature)
            .Select(f => f.Feature)
            .ToListAsync(ct);
    }

    public async Task<TenantFeatureFlagItem> DefinirAsync(
        Guid empresaId, string feature, bool ativo, string alteradoPor, CancellationToken ct = default)
    {
        var chave = Normalizar(feature);

        // Índice único é (EmpresaId, Feature) — a busca precisa dos dois, senão um tenant
        // sobrescreveria a flag de outro que tenha a mesma feature.
        var existente = await db.TenantFeatureFlags
            .FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Feature == chave, ct);

        if (existente is null)
        {
            existente = TenantFeatureFlag.Criar(empresaId, chave, ativo, alteradoPor);
            db.TenantFeatureFlags.Add(existente);
        }
        else
        {
            existente.Atualizar(ativo, alteradoPor);
        }

        await db.SaveChangesAsync(ct);
        return new TenantFeatureFlagItem(existente.Feature, existente.Ativo, existente.AlteradoEm, existente.AlteradoPor);
    }

    /// <summary>
    /// Nome canônico da feature: minúsculo e sem espaços nas pontas. Sem isso "Propostas" e
    /// "propostas" viram duas linhas — e o índice único não impede, porque ele compara os
    /// textos como vieram.
    /// </summary>
    private static string Normalizar(string feature) => (feature ?? string.Empty).Trim().ToLowerInvariant();
}
