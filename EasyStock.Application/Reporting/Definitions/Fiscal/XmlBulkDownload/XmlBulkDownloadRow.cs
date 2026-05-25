namespace EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;

/// <summary>
/// "Linha" de saída do relatório "XMLs autorizados (em lote)".
/// Cada item representa um XML de NFC-e a ser incluído no ZIP.
/// Consumida pelo <c>XmlBulkZipExporter</c> para baixar cada XML do storage
/// e adicioná-lo ao arquivo ZIP sem materializar todos em memória.
/// </summary>
public sealed record XmlBulkDownloadRow(
    string StorageKey,
    string NomeArquivo,
    string ChaveAcesso,
    DateTime DataAutorizacao,
    long Numero,
    short Serie);
