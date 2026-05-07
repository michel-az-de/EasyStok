using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Integration;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CredencialIntegracaoRepository(EasyStockDbContext db) : ICredencialIntegracaoRepository
{
    public Task<CredencialIntegracao?> GetAtivaAsync(
        Guid empresaId,
        string providerKey,
        AmbienteIntegracao ambiente,
        CancellationToken ct = default)
    {
        var key = (providerKey ?? string.Empty).Trim().ToLowerInvariant();
        return db.Set<CredencialIntegracao>()
            .Where(c => c.EmpresaId == empresaId
                     && c.ProviderKey == key
                     && c.Ambiente == ambiente
                     && c.Ativo)
            .FirstOrDefaultAsync(ct);
    }

    public Task<CredencialIntegracao?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => db.Set<CredencialIntegracao>().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<CredencialIntegracao>> ListarPorEmpresaAsync(
        Guid empresaId,
        CancellationToken ct = default)
    {
        return await db.Set<CredencialIntegracao>()
            .Where(c => c.EmpresaId == empresaId)
            .OrderByDescending(c => c.AlteradoEm)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CredencialIntegracao>> ListarPorKekAsync(
        string kekId,
        CancellationToken ct = default)
    {
        // IgnoreQueryFilters: rotação é admin cross-tenant. Caller (job batch)
        // já valida permissão SuperAdmin antes.
        return await db.Set<CredencialIntegracao>()
            .IgnoreQueryFilters()
            .Where(c => c.KekId == kekId && c.Ativo)
            .ToListAsync(ct);
    }

    public Task AddAsync(CredencialIntegracao credencial, CancellationToken ct = default)
    {
        db.Set<CredencialIntegracao>().Add(credencial);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(CredencialIntegracao credencial, CancellationToken ct = default)
    {
        db.Set<CredencialIntegracao>().Update(credencial);
        return Task.CompletedTask;
    }
}
