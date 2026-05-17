namespace EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;

/// <summary>
/// Linha de saída do relatório "Livro de Saídas (NFC-e)".
/// Cada linha representa um NfeDocumento (Autorizado ou Cancelado).
///
/// NOTA DE DADOS LEGADOS (PR-D):
/// NFC-e emitidas antes de AddNfceTaxFields têm <see cref="TributosRastreados"/> = false.
/// Nesse caso, BaseIcms, ValorIcms, Pis e Cofins são exibidos como R$ 0,00 com indicador
/// "Não rastreado" no arquivo gerado (ver aviso inserido no cabeçalho do relatório).
/// </summary>
public sealed record LivroSaidasRow(
    DateTime?  DataAutorizacao,
    long       Numero,
    short      Serie,
    string?    ChaveAcesso,
    string     Status,
    string?    DestinatarioNome,
    string?    CfopPrincipal,
    decimal    TotalNota,
    decimal    BaseIcms,
    decimal    ValorIcms,
    decimal    Pis,
    decimal    Cofins,
    bool       TributosRastreados);
