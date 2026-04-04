using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class FornecedorRepository(EasyStockDbContext dbContext) : IFornecedorRepository
    {
        public Task<Fornecedor?> GetByIdAsync(Guid id) =>
            dbContext.Fornecedores.FirstOrDefaultAsync(f => f.Id == id);

        public async Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize)
        {
            var query = dbContext.Fornecedores
                .AsNoTracking()
                .Where(f => f.EmpresaId == empresaId);

            var total = await query.CountAsync();
            var fornecedores = await query
                .OrderBy(f => f.Nome)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (fornecedores, total);
        }

        public Task AddAsync(Fornecedor fornecedor) =>
            dbContext.Fornecedores.AddAsync(fornecedor).AsTask();

        public Task UpdateAsync(Fornecedor fornecedor)
        {
            dbContext.Fornecedores.Update(fornecedor);
            return Task.CompletedTask;
        }
    }
}
