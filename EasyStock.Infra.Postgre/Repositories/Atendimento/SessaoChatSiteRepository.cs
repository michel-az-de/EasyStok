using EasyStock.Application.Ports.Output.Persistence.Atendimento;
using EasyStock.Domain.Entities.Atendimento;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories.Atendimento;

public sealed class SessaoChatSiteRepository(EasyStockDbContext db) : ISessaoChatSiteRepository
{
    public Task AddAsync(SessaoChatSite sessao, CancellationToken ct = default) =>
        db.SessoesChatSite.AddAsync(sessao, ct).AsTask();

    public Task<SessaoChatSite?> ObterPorTokenHashAsync(Guid empresaId, string tokenHash, CancellationToken ct = default) =>
        db.SessoesChatSite.FirstOrDefaultAsync(s => s.EmpresaId == empresaId && s.TokenHash == tokenHash, ct);

    public async Task<int> RemoverVencidasAsync(DateTime limiteUtc, CancellationToken ct = default)
    {
        // Cross-tenant: roda do serviço de limpeza, sem requisição e sem tenant.
        using var _ = db.UseRowLevelSecurityBypass();
        return await db.SessoesChatSite
            .IgnoreQueryFilters()
            .Where(s => s.ExpiraEm < limiteUtc)
            .ExecuteDeleteAsync(ct);
    }
}