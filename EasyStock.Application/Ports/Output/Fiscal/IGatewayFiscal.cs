using EasyStock.Domain.Fiscal;

namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Contrato generico para gateways fiscais (Focus NFe, eNotas, TecnoSpeed, mock).
/// Cada provedor implementa esta interface via adapter em
/// <c>EasyStock.Infra.Integrations.Fiscal/</c>. O <see cref="EmpresaConfiguracaoFiscal.ProvedorPreferido"/>
/// determina qual adapter usar.
///
/// <para>
/// <b>Importante:</b> chamadas a este gateway envolvem HTTP a um terceiro. Os use cases
/// devem orquestrar (a) commit DB com numero reservado e <see cref="StatusNfe.EnviadaAguardandoRetorno"/>,
/// (b) chamada gateway FORA de transacao (evitar manter tx aberta durante I/O remoto),
/// (c) commit do resultado (Autorizada/Rejeitada/FalhaTransiente).
/// </para>
/// </summary>
public interface IGatewayFiscal
{
    /// <summary>Identificador estavel do provedor — ex: "focus", "enotas", "mock".</summary>
    string Provedor { get; }

    /// <summary>
    /// Envia o documento ja preparado para a SEFAZ via gateway. NfeDocumento deve estar
    /// com <see cref="StatusNfe.EnviadaAguardandoRetorno"/> ou <see cref="StatusNfe.FalhaTransiente"/>
    /// (caso de reprocessamento). Retorna o resultado da emissao, mas a aplicacao do
    /// resultado ao agregado (MarcarAutorizada/MarcarRejeitada/MarcarFalhaTransiente) e
    /// responsabilidade do use case caller.
    /// </summary>
    /// <exception cref="GatewayFiscalTransienteException">Falha transiente (5xx, timeout, rede). Caller deve marcar FalhaTransiente e deixar job de contingencia reprocessar.</exception>
    /// <exception cref="GatewayFiscalRejeitadaException">SEFAZ rejeitou (4xx com codigo de rejeicao). Caller deve marcar Rejeitada com o motivo.</exception>
    /// <exception cref="GatewayFiscalCredencialException">Credencial invalida (401/403). Caller deve alertar admin do tenant.</exception>
    Task<ResultadoEmissaoNfce> EmitirAsync(NfeDocumento nfe, ConfigFiscalDto config, CancellationToken ct = default);

    /// <summary>
    /// Solicita cancelamento de uma NFC-e ja autorizada. Caller deve chamar
    /// ESTE METODO ANTES de commitar <see cref="StatusNfe.Cancelada"/> no agregado —
    /// se SEFAZ recusar (fora do prazo, etc.), use case lanca excecao e nao commita.
    /// </summary>
    /// <exception cref="GatewayFiscalRejeitadaException">SEFAZ recusou cancelamento (prazo expirado, nota ja cancelada, etc.).</exception>
    /// <exception cref="GatewayFiscalTransienteException">Falha transiente. Caller pode tentar de novo ou agendar.</exception>
    Task<ResultadoCancelamentoNfce> CancelarAsync(NfeDocumento nfe, string motivo, ConfigFiscalDto config, CancellationToken ct = default);

    /// <summary>
    /// Inutiliza uma faixa de numeracao (motivo: numero pulado, lote queimado etc.).
    /// Faixa [numeroInicial..numeroFinal] inclusive. SEFAZ aceita inutilizacao no
    /// mesmo ano fiscal apenas.
    /// </summary>
    Task<ResultadoInutilizacaoNfce> InutilizarAsync(
        Guid empresaId,
        short serie,
        long numeroInicial,
        long numeroFinal,
        string justificativa,
        ConfigFiscalDto config,
        CancellationToken ct = default);

    /// <summary>
    /// Consulta status atual na SEFAZ (usado por job de contingencia/reconciliacao
    /// quando webhook se perde ou crash entre HTTP e commit local).
    /// </summary>
    Task<ResultadoConsultaNfce> ConsultarStatusAsync(string chaveAcesso, ConfigFiscalDto config, CancellationToken ct = default);
}
