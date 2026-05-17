using EasyStock.Domain.Entities.Financeiro;
using EasyStock.Domain.Enums.Financeiro;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface IContaReceberRepository
{
    Task AddAsync(ContaReceber conta, CancellationToken ct = default);
    Task UpdateAsync(ContaReceber conta, CancellationToken ct = default);

    Task<ContaReceber?> GetByIdAsync(Guid empresaId, Guid id, CancellationToken ct = default);
    Task<ContaReceber?> GetByIdWithDetailsAsync(Guid empresaId, Guid id, CancellationToken ct = default);

    Task<ContaReceber?> GetByOrigemAsync(Guid empresaId, OrigemContaFinanceira origem, Guid origemRefId, CancellationToken ct = default);
    Task<ContaReceber?> GetByDocumentoReferenciaAsync(Guid empresaId, string documentoReferencia, CancellationToken ct = default);

    Task<(IReadOnlyList<ContaReceber> Itens, int Total)> ListarAsync(
        Guid empresaId,
        StatusContaFinanceira? status = null,
        Guid? clienteId = null,
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

    Task<ParcelaReceber?> GetParcelaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default);
    Task<ParcelaReceber?> GetParcelaWithContaAsync(Guid empresaId, Guid parcelaId, CancellationToken ct = default);

    /// <summary>Webhook usa essa busca cross-tenant via UNIQUE em <c>EfiTxid</c>.</summary>
    Task<ParcelaReceber?> GetParcelaByEfiTxidAsync(string txid, CancellationToken ct = default);

    Task AddEventoAsync(ContaFinanceiraEvento evento, CancellationToken ct = default);
    Task AddAlteracaoAsync(ContaReceberAlteracao alteracao, CancellationToken ct = default);

    Task<IReadOnlyList<ParcelaReceber>> ListarParcelasParaJobVencimentoAsync(
        DateTime hojeUtc, CancellationToken ct = default);

    /// <summary>Lista parcelas com Pix ativo pendente de reconciliacao.</summary>
    Task<IReadOnlyList<ParcelaReceber>> ListarParcelasComPixAtivoAsync(
        DateTime hojeUtc, CancellationToken ct = default);

    Task<IReadOnlyDictionary<StatusContaFinanceira, decimal>> SomarPorStatusAsync(
        Guid empresaId, DateTime de, DateTime ate, CancellationToken ct = default);
}
