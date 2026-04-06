using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class VendaRepository(EasyStockDbContext dbContext)
        : IVendaRepository
    {
        public Task<Venda?> GetByIdAsync(Guid id) =>
            dbContext.Vendas.FirstOrDefaultAsync(v => v.Id == id);

        public Task<Venda?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.Vendas
                .AsNoTracking()
                .FirstOrDefaultAsync(v => v.EmpresaId == empresaId && v.Id == id);

        public async Task<(IEnumerable<Venda> Vendas, int TotalCount)> GetVendasPorEmpresaAsync(Guid empresaId, int page = 1, int pageSize = 20)
        {
            var query = dbContext.Vendas
                .AsNoTracking()
                .Where(v => v.EmpresaId == empresaId)
                .OrderByDescending(v => v.DataVenda);

            var totalCount = await query.CountAsync();
            var vendas = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
            return (vendas, totalCount);
        }

        public Task InsertAsync(Venda venda) =>
            dbContext.Vendas.AddAsync(venda).AsTask();
    }
}
