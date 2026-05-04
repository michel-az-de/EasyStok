using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IEmpresaRepository
    {
        Task<Empresa?> GetByIdAsync(Guid id);
        Task<Empresa?> GetByDocumentoAsync(string documento);
        Task<IEnumerable<Empresa>> GetAllAsync();
        /// <summary>
        /// Itera todas as empresas em streaming, sem carregar tudo em memoria.
        /// Preferir sobre GetAllAsync em background jobs e relatorios.
        /// </summary>
        IAsyncEnumerable<Empresa> StreamAllAsync(CancellationToken ct = default);
        Task AddAsync(Empresa empresa);
    }
}
