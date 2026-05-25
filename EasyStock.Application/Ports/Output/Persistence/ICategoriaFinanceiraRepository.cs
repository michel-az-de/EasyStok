using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface ICategoriaFinanceiraRepository
{
    Task AddAsync(CategoriaFinanceira categoria, CancellationToken ct = default);
    Task UpdateAsync(CategoriaFinanceira categoria, CancellationToken ct = default);

    Task<CategoriaFinanceira?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<CategoriaFinanceira>> ListarAsync(
        Guid empresaId,
        bool? ativa = null,
        TipoCategoriaFinanceira? tipo = null,
        CancellationToken ct = default);

    /// <summary>True se ja existe categoria com o mesmo nome no mesmo parent (filtrado em ativas).</summary>
    Task<bool> ExisteNomeAsync(Guid empresaId, string nome, Guid? parentId, Guid? excludeId = null, CancellationToken ct = default);

    /// <summary>True se ha conta a pagar ou receber NAO cancelada usando essa categoria.</summary>
    Task<bool> ExisteContaAbertaAsync(Guid empresaId, Guid categoriaId, CancellationToken ct = default);
}
