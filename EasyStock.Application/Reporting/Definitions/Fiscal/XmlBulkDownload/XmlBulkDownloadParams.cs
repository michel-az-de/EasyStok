namespace EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;

/// <summary>
/// Parâmetros do relatório "XMLs autorizados (em lote)".
/// Gera um arquivo ZIP com todos os XMLs de NFC-e autorizadas no período.
/// </summary>
public sealed record XmlBulkDownloadParams(
    DateOnly De,
    DateOnly Ate);
