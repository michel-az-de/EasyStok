using System.Runtime.CompilerServices;
using System.Text.Json;
using EasyStock.Application.Reporting;
using EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;
using EasyStock.Domain.Fiscal;
using EasyStock.Domain.Reporting;
using EasyStock.Infra.Postgre.Data;
using Microsoft.EntityFrameworkCore;

namespace EasyStock.Infra.Async.Reporting.Handlers.Fiscal;

/// <summary>
/// Handler do relatório "XMLs autorizados (em lote)" — Fase 2.
/// Não produz linhas tabulares — cada <see cref="XmlBulkDownloadRow"/> carrega a chave de storage
/// do XML assinado para que o <c>XmlBulkZipExporter</c> componha o ZIP em streaming,
/// sem carregar todos os XMLs em memória simultaneamente.
/// </summary>
public sealed class XmlBulkDownloadHandler(
    ITenantScopedQueryBuilder tenantQuery)
    : IReportHandler<XmlBulkDownloadParams, XmlBulkDownloadRow>
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public ReportSchema GetSchema(XmlBulkDownloadParams parametros)
    {
        // Esquema mínimo — o exporter ZIP não renderiza colunas tabulates.
        var competencia = $"{parametros.De:yyyy-MM}";
        return new ReportSchema(
            title:        "XMLs autorizados (em lote)",
            fileNameBase: $"xmls-nfce_{competencia}",
            columns:      []);
    }

    public async Task ValidateAsync(XmlBulkDownloadParams parametros, CancellationToken ct)
    {
        if (parametros.De > parametros.Ate)
            throw new ArgumentException(
                "A data final deve ser igual ou posterior à inicial.",
                nameof(parametros.Ate));

        if (parametros.Ate.DayNumber - parametros.De.DayNumber > 366)
            throw new ArgumentException(
                "Para períodos maiores que 12 meses, divida em gerações mensais.",
                nameof(parametros.Ate));
    }

    public async IAsyncEnumerable<XmlBulkDownloadRow> StreamAsync(
        XmlBulkDownloadParams parametros,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var de  = parametros.De.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var ate = parametros.Ate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);

        // Apenas NFC-e autorizadas com XML disponível no storage.
        var documentos = tenantQuery.Query<NfeDocumento>()
            .Where(n => n.Status == StatusNfe.Autorizada
                        && n.DataAutorizacao >= de && n.DataAutorizacao <= ate
                        && n.XmlAssinadoStorageKey != null)
            .OrderBy(n => n.DataAutorizacao)
            .Select(n => new
            {
                n.Numero,
                n.Serie,
                n.ChaveAcesso,
                n.DataAutorizacao,
                n.XmlAssinadoStorageKey,
            })
            .AsNoTracking()
            .AsAsyncEnumerable();

        await foreach (var doc in documentos.WithCancellation(ct))
        {
            // Nome do arquivo dentro do ZIP: {chave-acesso}.xml ou {nº}_{serie}.xml como fallback.
            var nomeArquivo = doc.ChaveAcesso is { Length: > 0 }
                ? $"{doc.ChaveAcesso}.xml"
                : $"{doc.Numero:D9}_{doc.Serie:D3}.xml";

            yield return new XmlBulkDownloadRow(
                StorageKey:      doc.XmlAssinadoStorageKey!,
                NomeArquivo:     nomeArquivo,
                ChaveAcesso:     doc.ChaveAcesso ?? string.Empty,
                DataAutorizacao: doc.DataAutorizacao!.Value,
                Numero:          doc.Numero,
                Serie:           doc.Serie);
        }
    }

    public XmlBulkDownloadParams DeserializeParams(string paramsJson) =>
        JsonSerializer.Deserialize<XmlBulkDownloadParams>(paramsJson, _jsonOptions)
            ?? throw new InvalidOperationException("Falha ao deserializar XmlBulkDownloadParams.");
}
