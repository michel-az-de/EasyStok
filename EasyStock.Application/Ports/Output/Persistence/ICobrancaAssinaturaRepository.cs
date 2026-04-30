using EasyStock.Domain.Entities;

namespace EasyStock.Application.Ports.Output.Persistence;

public interface ICobrancaAssinaturaRepository
{
    Task AddAsync(CobrancaAssinatura cobranca);
    Task UpdateAsync(CobrancaAssinatura cobranca);
    Task<CobrancaAssinatura?> GetByTxidAsync(string txid);
    Task<bool> ExistePendenteAsync(Guid empresaId);
}
