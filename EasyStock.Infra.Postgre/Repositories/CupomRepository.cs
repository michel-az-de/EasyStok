using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class CupomRepository(EasyStockDbContext dbContext) : ICupomRepository
    {
        public Task<Cupom?> GetByCodigoAsync(string codigo)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return Task.FromResult<Cupom?>(null);
            var normalizado = codigo.ToUpperInvariant();
            return dbContext.Cupons.FirstOrDefaultAsync(c => c.Codigo == normalizado);
        }
    }
}
