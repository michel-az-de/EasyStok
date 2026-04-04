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

        public Task AddAsync(AssinaturaEmpresa assinatura) =>
            dbContext.AssinaturasEmpresa.AddAsync(assinatura).AsTask();

        public Task UpdateAsync(AssinaturaEmpresa assinatura)
        {
            dbContext.AssinaturasEmpresa.Update(assinatura);
            return Task.CompletedTask;
        }
    }
}
