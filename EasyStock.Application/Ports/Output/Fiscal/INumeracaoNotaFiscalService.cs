using EasyStock.Domain.Enums.Fiscal;

namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Reserva próximo número fiscal por (empresa, loja, modelo, serie).
/// Implementação usa SELECT FOR UPDATE em nota_fiscal_contador para
/// serialização atômica (ADR-004). Deve ser chamado dentro de uma
/// transação para evitar liberação prematura do lock.
/// </summary>
public interface INumeracaoNotaFiscalService
{
    Task<int> ReservarProximoNumeroAsync(
        Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie, CancellationToken ct);

    Task<int> ObterUltimoNumeroAsync(
        Guid empresaId, Guid lojaId, ModeloDocumentoFiscal modelo, int serie, CancellationToken ct);
}
