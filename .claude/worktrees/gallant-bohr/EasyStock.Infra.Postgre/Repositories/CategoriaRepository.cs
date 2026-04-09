using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Domain.Entities;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class CategoriaRepository(EasyStockDbContext dbContext)
        : ICategoriaRepository
    {
        public Task<Categoria?> GetByIdAsync(Guid id) =>
            dbContext.Categorias
                .Include(c => c.SubCategorias)
                .FirstOrDefaultAsync(c => c.Id == id);

        public async Task<IEnumerable<Categoria>> GetByEmpresaAsync(Guid empresaId) =>
            await dbContext.Categorias
                .AsNoTracking()
                .Include(c => c.SubCategorias)
                .Where(c => c.EmpresaId == empresaId && c.CategoriaPaiId == null)
                .OrderBy(c => c.Nome)
                .ToListAsync();

        public Task<bool> ExisteProdutosNaCategoriaAsync(Guid categoriaId) =>
            dbContext.Produtos.AnyAsync(p => p.CategoriaId == categoriaId);

        public Task AddAsync(Categoria categoria) =>
            dbContext.Categorias.AddAsync(categoria).AsTask();

        public Task UpdateAsync(Categoria categoria)
        {
            dbContext.Categorias.Update(categoria);
            return Task.CompletedTask;
        }

        public async Task DeleteAsync(Guid id)
        {
            var categoria = await dbContext.Categorias.FindAsync(id);
            if (categoria != null)
                dbContext.Categorias.Remove(categoria);
        }
    }
}
