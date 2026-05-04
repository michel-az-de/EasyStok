using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class AssinaturaEmpresaRepository(EasyStockDbContext dbContext) : IAssinaturaEmpresaRepository
    {
        public async Task<IEnumerable<AssinaturaEmpresa>> GetByEmpresaAsync(Guid empresaId) =>
            await dbContext.AssinaturasEmpresa
                .AsNoTracking()
                .Include(a => a.Plano)
                .Where(a => a.EmpresaId == empresaId)
                .ToListAsync();

        public Task<AssinaturaEmpresa?> GetAtivaAsync(Guid empresaId) =>
            dbContext.AssinaturasEmpresa
                .Include(a => a.Plano)
                .FirstOrDefaultAsync(a => a.EmpresaId == empresaId && a.Status == StatusAssinatura.Ativa);

        public Task<AssinaturaEmpresa?> GetMaisRecenteAsync(Guid empresaId) =>
            dbContext.AssinaturasEmpresa
                .Where(a => a.EmpresaId == empresaId)
                .OrderByDescending(a => a.DataInicio)
                .FirstOrDefaultAsync();

        public Task<AssinaturaEmpresa?> GetAtivaMaisRecenteAsync(Guid empresaId) =>
            dbContext.AssinaturasEmpresa
                .Include(a => a.Plano)
                .Where(a => a.EmpresaId == empresaId && a.Status == StatusAssinatura.Ativa)
                .OrderByDescending(a => a.DataInicio)
                .FirstOrDefaultAsync();

        public Task AddAsync(AssinaturaEmpresa assinatura) =>
            dbContext.AssinaturasEmpresa.AddAsync(assinatura).AsTask();

        public Task UpdateAsync(AssinaturaEmpresa assinatura)
        {
            dbContext.AssinaturasEmpresa.Update(assinatura);
            return Task.CompletedTask;
        }

        public async Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencendoEmAsync(int diasAte, CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            var limite = now.AddDays(diasAte);
            return await dbContext.AssinaturasEmpresa
                .Include(a => a.Empresa)
                .Where(a => a.Status == StatusAssinatura.Ativa &&
                    ((a.TrialFim != null && a.TrialFim >= now && a.TrialFim <= limite) ||
                     (a.DataFim != null && a.DataFim >= now && a.DataFim <= limite)))
                .ToListAsync(ct);
        }

        public async Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencidasAsync(CancellationToken ct = default)
        {
            var now = DateTime.UtcNow;
            return await dbContext.AssinaturasEmpresa
                .Where(a => a.Status == StatusAssinatura.Ativa &&
                    ((a.TrialFim != null && a.TrialFim < now) ||
                     (a.DataFim != null && a.DataFim < now)))
                .ToListAsync(ct);
        }

        public async Task<IEnumerable<AssinaturaEmpresa>> GetSuspensasAntigasAsync(int diasMinimos, CancellationToken ct = default)
        {
            var limite = DateTime.UtcNow.AddDays(-diasMinimos);
            return await dbContext.AssinaturasEmpresa
                .Where(a => a.Status == StatusAssinatura.Suspensa
                         && ((a.SuspensaEm != null && a.SuspensaEm < limite)
                          || (a.SuspensaEm == null && a.AlteradoEm < limite)))
                .ToListAsync(ct);
        }
    }
}
