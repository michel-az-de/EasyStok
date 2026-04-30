using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IAssinaturaEmpresaRepository
    {
        Task<IEnumerable<AssinaturaEmpresa>> GetByEmpresaAsync(Guid empresaId);
        Task<AssinaturaEmpresa?> GetAtivaAsync(Guid empresaId);
        Task AddAsync(AssinaturaEmpresa assinatura);
        Task UpdateAsync(AssinaturaEmpresa assinatura);
        Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencendoEmAsync(int diasAte, CancellationToken ct = default);
        Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencidasAsync(CancellationToken ct = default);
    }
}
