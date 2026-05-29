namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IPerfilRepository
    {
        Task<Perfil?> GetByIdAsync(Guid id);
        Task<IEnumerable<Perfil>> GetPadroesAsync();
        Task<IEnumerable<Perfil>> GetByEmpresaAsync(Guid empresaId);
        Task AddAsync(Perfil perfil);
    }
}
