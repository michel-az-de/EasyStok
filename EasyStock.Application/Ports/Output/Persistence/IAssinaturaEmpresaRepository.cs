using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence
{
    public interface IAssinaturaEmpresaRepository
    {
        /// <summary>Busca assinatura pelo id (sem filtro de empresa).</summary>
        Task<AssinaturaEmpresa?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<IEnumerable<AssinaturaEmpresa>> GetByEmpresaAsync(Guid empresaId);
        Task<AssinaturaEmpresa?> GetAtivaAsync(Guid empresaId);
        /// <summary>Assinatura mais recente da empresa (qualquer status), ordenada por DataInicio desc.</summary>
        Task<AssinaturaEmpresa?> GetMaisRecenteAsync(Guid empresaId);
        /// <summary>Assinatura ativa mais recente, ordenada por DataInicio desc.</summary>
        Task<AssinaturaEmpresa?> GetAtivaMaisRecenteAsync(Guid empresaId);
        Task AddAsync(AssinaturaEmpresa assinatura);
        Task UpdateAsync(AssinaturaEmpresa assinatura);
        Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencendoEmAsync(int diasAte, CancellationToken ct = default);
        Task<IEnumerable<AssinaturaEmpresa>> GetAtivasVencidasAsync(CancellationToken ct = default);
        Task<IEnumerable<AssinaturaEmpresa>> GetSuspensasAntigasAsync(int diasMinimos, CancellationToken ct = default);
    }
}
