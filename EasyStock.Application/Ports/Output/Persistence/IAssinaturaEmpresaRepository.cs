using EasyStock.Domain.Entities;
using EasyStock.Domain.Enums;

namespace EasyStock.Application.Ports.Output.Persistence;

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

    /// <summary>
    /// Soma <c>Plano.PrecoMensal</c> de todas assinaturas Ativas (com Plano
    /// JOIN). Base para calculo de MRR (F10). Retorna 0 se nao ha
    /// assinaturas ativas. Quando <paramref name="empresaId"/> for informado,
    /// limita ao tenant — evita vazar MRR global pra admin operacional.
    /// </summary>
    Task<decimal> SomarPrecoMensalAtivasAsync(Guid? empresaId = null, CancellationToken ct = default);

    /// <summary>
    /// Conta assinaturas por status — usado para MRR + churn snapshot.
    /// Filtro opcional por empresa (multi-tenant): quando <paramref name="empresaId"/>
    /// for informado, retorna apenas o tenant.
    /// </summary>
    Task<IReadOnlyDictionary<StatusAssinatura, int>> ContarPorStatusAsync(
        Guid? empresaId = null, CancellationToken ct = default);
}
