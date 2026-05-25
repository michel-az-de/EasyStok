using EasyStock.Domain.Entities.Financeiro;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface ICentroCustoRepository
{
    Task AddAsync(CentroCusto centro, CancellationToken ct = default);
    Task UpdateAsync(CentroCusto centro, CancellationToken ct = default);

    Task<CentroCusto?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    Task<CentroCusto?> GetByCodigoAsync(Guid empresaId, string codigo, CancellationToken ct = default);

    Task<IReadOnlyList<CentroCusto>> ListarAsync(
        Guid empresaId,
        bool? ativo = null,
        Guid? lojaId = null,
        CancellationToken ct = default);

    Task<bool> ExisteContaAbertaAsync(Guid empresaId, Guid centroCustoId, CancellationToken ct = default);
}
