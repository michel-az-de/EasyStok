namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Servico de numeracao de NFC-e. Reserva proximo numero sob lock pessimista
/// (<c>SELECT FOR UPDATE</c>) na tabela <c>empresa_configuracao_fiscal</c>,
/// garantindo que duas emissoes paralelas no mesmo tenant recebem numeros
/// distintos.
///
/// <para>
/// <b>Uso:</b> deve ser chamado DENTRO de <see cref="EasyStock.Application.Ports.Output.Persistence.IUnitOfWork.ExecuteInTransactionAsync"/>
/// — o lock acompanha a transacao e e liberado no commit. Caller deve fazer:
/// (a) BeginTx, (b) ReservarProximoNumero, (c) criar NfeDocumento, (d) Commit.
/// </para>
/// </summary>
public interface INumeracaoNfeService
{
    /// <summary>
    /// Reserva o proximo numero da serie configurada em <see cref="EasyStock.Domain.Fiscal.EmpresaConfiguracaoFiscal.SerieNfce"/>.
    /// Incrementa <see cref="EasyStock.Domain.Fiscal.EmpresaConfiguracaoFiscal.ProximoNumeroNfce"/> sob lock.
    /// </summary>
    /// <param name="empresaId">Tenant da emissao.</param>
    /// <returns>(serie, numero) reservados. Serie da config; numero unico no tenant.</returns>
    Task<(short serie, long numero)> ReservarProximoNumeroAsync(Guid empresaId, CancellationToken ct = default);
}
