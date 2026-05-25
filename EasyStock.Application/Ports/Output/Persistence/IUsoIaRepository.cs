using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IUsoIaRepository
    {
        Task<UsoIa?> GetAsync(Guid empresaId, int ano, int mes);
        Task AddAsync(UsoIa uso);
        Task UpdateAsync(UsoIa uso);
    }
}
