using EasyStock.Application.Ports.Output.Persistence;
using EasyStock.Infra.Postgre.Data;

namespace EasyStock.Infra.Postgre.Repositories
{
    public sealed class UsoIaRepository(EasyStockDbContext dbContext) : IUsoIaRepository
    {
        public Task<UsoIa?> GetAsync(Guid empresaId, int ano, int mes) =>
            dbContext.UsoIa
                .FirstOrDefaultAsync(u => u.EmpresaId == empresaId && u.Ano == ano && u.Mes == mes);

        public Task AddAsync(UsoIa uso) =>
            dbContext.UsoIa.AddAsync(uso).AsTask();

        public Task UpdateAsync(UsoIa uso)
        {
            dbContext.UsoIa.Update(uso);
            return Task.CompletedTask;
        }
    }
}
