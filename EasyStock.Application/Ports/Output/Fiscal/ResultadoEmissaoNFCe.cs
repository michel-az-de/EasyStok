namespace EasyStock.Application.Ports.Output.Fiscal;

public enum ResultadoEmissao
{
    Autorizada,
    Rejeitada,
    Denegada,
}

/// <summary>
/// Resultado de uma chamada ao gateway fiscal. Quando <see cref="Resultado"/>
/// é <see cref="ResultadoEmissao.Autorizada"/>, <see cref="Protocolo"/>,
/// <see cref="XmlAutorizado"/> e <see cref="DhAutorizacao"/> são preenchidos.
/// Em rejeicao/denegacao, <see cref="Codigo"/> e <see cref="Motivo"/> são
/// preenchidos com cStat/xMotivo da SEFAZ.
/// </summary>
public sealed record ResultadoEmissaoNFCe(
    ResultadoEmissao Resultado,
    string? Protocolo,
    string? XmlAutorizado,
    DateTime? DhAutorizacao,
    string? UrlDanfeFiscal,
    string? UrlConsultaQr,
    string? Codigo,
    string? Motivo);

public sealed record ResultadoCancelamentoNFCe(
    bool Sucesso,
    string? Protocolo,
    string? XmlEvento,
    DateTime? DhCancelamento,
    string? Codigo,
    string? Motivo);

public sealed record ResultadoInutilizacaoNFCe(
    bool Sucesso,
    string? Protocolo,
    string? XmlEvento,
    string? Codigo,
    string? Motivo);
