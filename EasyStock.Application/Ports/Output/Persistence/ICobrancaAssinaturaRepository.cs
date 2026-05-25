using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface ICobrancaAssinaturaRepository
{
    Task AddAsync(CobrancaAssinatura cobranca);
    Task UpdateAsync(CobrancaAssinatura cobranca);
    Task<CobrancaAssinatura?> GetByTxidAsync(string txid);

    /// <summary>
    /// FOR UPDATE em <c>Txid</c>. Use dentro de transacao explicita para serializar
    /// duplo-fire de webhook Pix (Efi reenvia ate 5x dentro de 5 min). Sem isso,
    /// dois webhooks simultaneos passam o check de status Pendente e renovam a
    /// assinatura em duplicidade — 60 dias de vigencia por 1 pagamento.
    /// </summary>
    Task<CobrancaAssinatura?> GetByTxidComLockAsync(string txid, CancellationToken ct = default);
    Task<bool> ExistePendenteAsync(Guid empresaId);
    Task<IEnumerable<CobrancaAssinatura>> GetByEmpresaAsync(Guid empresaId, int limit = 24);
    Task<IEnumerable<CobrancaAssinatura>> GetPendentesParaDunningAsync(CancellationToken ct = default);
}
