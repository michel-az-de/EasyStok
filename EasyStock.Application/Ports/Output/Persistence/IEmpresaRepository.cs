using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IEmpresaRepository
    {
        Task<Empresa?> GetByIdAsync(Guid id);
        Task<Empresa?> GetByDocumentoAsync(string documento);
        Task<IEnumerable<Empresa>> GetAllAsync();
        Task AddAsync(Empresa empresa);
    }
}
