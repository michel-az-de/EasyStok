using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class EmpresaRepository(EasyStockDbContext dbContext)
        : IEmpresaRepository
    {
        public Task<Empresa?> GetByIdAsync(Guid id) =>
            dbContext.Empresas.FirstOrDefaultAsync(e => e.Id == id);

        public Task<Empresa?> GetByDocumentoAsync(string documento) =>
            string.IsNullOrWhiteSpace(documento)
                ? Task.FromResult<Empresa?>(null)
                : dbContext.Empresas.AsNoTracking()
                    .FirstOrDefaultAsync(e => e.Documento == documento);

        public async Task<IEnumerable<Empresa>> GetAllAsync() =>
            await dbContext.Empresas.AsNoTracking().ToListAsync();

        public IAsyncEnumerable<Empresa> StreamAllAsync(CancellationToken ct = default) =>
            dbContext.Empresas.AsNoTracking().AsAsyncEnumerable();

        public Task AddAsync(Empresa empresa) =>
            dbContext.Empresas.AddAsync(empresa).AsTask();
    }
}
