using EasyStock.Domain.Entities.Fiscal;

namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Port do gateway fiscal. Hoje implementado por FocusNFeAdapter. Trocar
/// pra eNotas/PlugNotas amanhã = novo adapter sem mudar Application.
/// Ver ADR-001.
/// </summary>
public interface IGatewayFiscal
{
    Task<ResultadoEmissaoNFCe> EmitirNFCeAsync(
        NotaFiscal nota, ConfigFiscalDto config, CancellationToken ct);

    Task<ResultadoCancelamentoNFCe> CancelarNFCeAsync(
        NotaFiscal nota, string justificativa, ConfigFiscalDto config, CancellationToken ct);

    Task<ResultadoInutilizacaoNFCe> InutilizarNumeracaoAsync(
        NotaFiscalInutilizacao inutilizacao, ConfigFiscalDto config, CancellationToken ct);

    Task<ResultadoEmissaoNFCe> RetransmitirContingenciaAsync(
        NotaFiscal nota, ConfigFiscalDto config, CancellationToken ct);

    /// <summary>
    /// Gera XML local sem assinatura (Focus assina ao retransmitir).
    /// Usado em contingência offline para preservar o documento.
    /// </summary>
    string GerarXmlAssinadoLocal(NotaFiscal nota, ConfigFiscalDto config);
}
