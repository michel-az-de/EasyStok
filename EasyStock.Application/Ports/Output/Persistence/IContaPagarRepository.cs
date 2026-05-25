using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IContaPagarRepository
{
    Task AddAsync(ContaPagar conta, CancellationToken ct = default);
    Task UpdateAsync(ContaPagar conta, CancellationToken ct = default);

    Task<ContaPagar?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    Task<ContaPagar?> GetByIdWithDetailsAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<ContaPagar?> GetByOrigemAsync(Guid empresaId, OrigemContaFinanceira origem, Guid origemRefId, CancellationToken ct = default);
    Task<ContaPagar?> GetByDocumentoReferenciaAsync(Guid empresaId, string documentoReferencia, CancellationToken ct = default);

    Task<(IReadOnlyList<ContaPagar> Itens, int Total)> ListarAsync(
        Guid empresaId,
        StatusContaFinanceira? status = null,
        Guid? fornecedorId = null,
        Guid? categoriaId = null,
        Guid? centroCustoId = null,
        DateTime? vencimentoDe = null,
        DateTime? vencimentoAte = null,
        string? busca = null,
        int page = 1,
        int pageSize = 20,
        string? sort = "datavencimento",
        string? order = "asc",
        CancellationToken ct = default);

    Task<ParcelaPagar?> GetParcelaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default);
    Task<ParcelaPagar?> GetParcelaWithContaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default);

    Task AddEventoAsync(ContaFinanceiraEvento evento, CancellationToken ct = default);
    Task AddAlteracaoAsync(ContaPagarAlteracao alteracao, CancellationToken ct = default);

    /// <summary>Lista parcelas globais (cross-tenant) pra job de vencimento.</summary>
    Task<IReadOnlyList<ParcelaPagar>> ListarParcelasParaJobVencimentoAsync(
        DateTime hojeUtc, CancellationToken ct = default);

    /// <summary>Soma de valores agregada por status, no periodo informado.</summary>
    Task<IReadOnlyDictionary<StatusContaFinanceira, decimal>> SomarPorStatusAsync(
        Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default);
}
