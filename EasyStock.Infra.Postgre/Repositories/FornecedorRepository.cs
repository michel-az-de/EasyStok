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

        public Task<Fornecedor?> GetByIdAsync(Guid empresaId, Guid id) =>
            dbContext.Fornecedores.FirstOrDefaultAsync(f => f.EmpresaId == empresaId && f.Id == id);

        public async Task<(IEnumerable<Fornecedor>, int total)> GetByEmpresaAsync(Guid empresaId, int page, int pageSize, bool? ativo = null, string? search = null)
        {
            var query = dbContext.Fornecedores
                .AsNoTracking()
                .Where(f => f.EmpresaId == empresaId);

            if (ativo.HasValue)
                query = query.Where(f => f.Ativo == ativo.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var termo = search.Trim();
                query = query.Where(f =>
                    EF.Functions.ILike(f.Nome, $"%{termo}%") ||
                    (f.Documento != null && EF.Functions.ILike(f.Documento, $"%{termo}%")) ||
                    (f.Email != null && EF.Functions.ILike(f.Email, $"%{termo}%")) ||
                    (f.Contato != null && EF.Functions.ILike(f.Contato, $"%{termo}%")));
            }

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
