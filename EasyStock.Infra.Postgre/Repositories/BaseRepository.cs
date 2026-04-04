using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Postgre.Repositories
{
    public abstract class BaseRepository<T> : IBaseRepository<T> where T : class
    {
        protected BaseRepository(EasyStockDbContext dbContext)
        {
            DbContext = dbContext;
            Set = dbContext.Set<T>();
        }

        protected EasyStockDbContext DbContext { get; }
        protected DbSet<T> Set { get; }

        public virtual async Task<T?> GetByIdAsync(Guid id) => await Set.FindAsync(id);

        public virtual async Task<IEnumerable<T>> GetAllAsync() => await Set.ToListAsync();

        public virtual async Task AddAsync(T entity) => await Set.AddAsync(entity);

        public virtual Task UpdateAsync(T entity)
        {
            Set.Update(entity);
            return Task.CompletedTask;
        }

        public virtual async Task DeleteAsync(Guid id)
        {
            var entity = await GetByIdAsync(id);
            if (entity is null) return;

            Set.Remove(entity);
        }
    }
}
