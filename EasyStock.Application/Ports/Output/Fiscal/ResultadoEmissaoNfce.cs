namespace EasyStock.Application.Ports.Output.Fiscal;

/// <summary>
/// Resultado bem-sucedido de uma emissao NFC-e via <see cref="IGatewayFiscal.EmitirAsync"/>.
/// Inclui chave de acesso, protocolo e URL do DANFE quando disponiveis.
///
/// <para>
/// Caso de rejeicao ou falha transiente NAO retorna este record — o gateway lanca
/// <see cref="GatewayFiscalRejeitadaException"/> ou <see cref="GatewayFiscalTransienteException"/>.
/// </para>
/// </summary>
public sealed record ResultadoEmissaoNfce(
    string ChaveAcesso,
    string ProtocoloAutorizacao,
    DateTime DataAutorizacao,
    string? XmlAssinadoUrl,
    string? DanfeUrl);

/// <summary>Resultado bem-sucedido de cancelamento.</summary>
public sealed record ResultadoCancelamentoNfce(
    string ProtocoloEvento,
    DateTime DataCancelamento);

/// <summary>Resultado bem-sucedido de inutilizacao.</summary>
public sealed record ResultadoInutilizacaoNfce(
    string ProtocoloEvento,
    DateTime DataInutilizacao);

/// <summary>Resultado de consulta de status. Status do gateway pode nao corresponder 1:1 ao StatusNfe — caller mapeia.</summary>
public sealed record ResultadoConsultaNfce(
    string StatusGateway,
    string? ProtocoloAutorizacao,
    string? MotivoRejeicao,
    DateTime? DataAutorizacao,
    string? XmlAssinadoUrl,
    string? DanfeUrl);
