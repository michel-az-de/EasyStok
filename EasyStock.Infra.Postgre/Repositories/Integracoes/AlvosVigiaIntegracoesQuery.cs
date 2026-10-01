using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Application.UseCases.FeatureFlags;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Integracoes;

/// <summary>
/// Empresas do vigia das integrações (F16, #1246): módulo de atendimento ligado ou alguma chave
/// de integração ativa. Cross-tenant: o host liga o bypass de RLS e <c>IgnoreQueryFilters</c>
/// tira o filtro global de tenant.
/// </summary>
public sealed class AlvosVigiaIntegracoesQuery(EasyStockDbContext db) : IAlvosVigiaIntegracoesQuery
{
    public async Task<IReadOnlyList<Guid>> ListarEmpresasAsync(CancellationToken ct = default)
    {
        var comAtendimento = db.TenantFeatureFlags.IgnoreQueryFilters()
            .Where(f => f.Feature == FeatureCatalogo.ModuloAtendimento && f.Ativo)
            .Select(f => f.EmpresaId);
        var comChave = db.CredenciaisIntegracao.IgnoreQueryFilters()
            .Where(c => c.Ativo)
            .Select(c => c.EmpresaId);

        return await comAtendimento.Union(comChave).Distinct().ToListAsync(ct);
    }
}
