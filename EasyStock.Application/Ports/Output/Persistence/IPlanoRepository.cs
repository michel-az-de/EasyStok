namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IPlanoRepository
    {
        Task<Plano?> GetByIdAsync(Guid id);
        Task<IEnumerable<Plano>> GetAtivosAsync();
        Task AddAsync(Plano plano);
    }
}
