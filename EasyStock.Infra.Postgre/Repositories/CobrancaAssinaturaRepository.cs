using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories;

public sealed class CobrancaAssinaturaRepository(EasyStockDbContext dbContext) : ICobrancaAssinaturaRepository
{
    public Task AddAsync(CobrancaAssinatura cobranca) =>
        dbContext.CobrancasAssinatura.AddAsync(cobranca).AsTask();

    public Task UpdateAsync(CobrancaAssinatura cobranca)
    {
        dbContext.CobrancasAssinatura.Update(cobranca);
        return Task.CompletedTask;
    }

    public Task<CobrancaAssinatura?> GetByTxidAsync(string txid) =>
        dbContext.CobrancasAssinatura
            .FirstOrDefaultAsync(c => c.Txid == txid);

    public Task<bool> ExistePendenteAsync(Guid empresaId) =>
        dbContext.CobrancasAssinatura
            .AnyAsync(c => c.EmpresaId == empresaId && c.Status == StatusCobranca.Pendente);

    public async Task<IEnumerable<CobrancaAssinatura>> GetByEmpresaAsync(Guid empresaId, int limit = 24) =>
        await dbContext.CobrancasAssinatura
            .AsNoTracking()
            .Where(c => c.EmpresaId == empresaId)
            .OrderByDescending(c => c.CriadoEm)
            .Take(limit)
            .ToListAsync();

    public async Task<IEnumerable<CobrancaAssinatura>> GetPendentesParaDunningAsync(CancellationToken ct = default)
    {
        // Cobranças pendentes de assinaturas suspensas — candidatas para dunning.
        return await dbContext.CobrancasAssinatura
            .Include(c => c.Assinatura)
            .Where(c => c.Status == StatusCobranca.Pendente
                     && c.Assinatura != null
                     && c.Assinatura.Status == StatusAssinatura.Suspensa
                     && c.TentativasLembrete < 4)
            .ToListAsync(ct);
    }
}
