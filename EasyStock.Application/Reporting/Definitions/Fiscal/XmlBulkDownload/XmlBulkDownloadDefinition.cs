using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Fiscal.XmlBulkDownload;

/// <summary>
/// Metadados estáticos do relatório "XMLs autorizados (em lote)".
/// Formato único: ZIP. Exporter especial <c>XmlBulkZipExporter</c>.
/// </summary>
public sealed class XmlBulkDownloadDefinition : IReportDefinition
{
    public string          Key                 => "nfce.xml-bulk-download";
    public ReportCategoria Categoria           => ReportCategoria.Fiscal;
    public ReportContexto  Contexto            => ReportContexto.Tenant;
    public string          Label               => "XMLs autorizados (em lote)";
    public string          Descricao           => "Pacote ZIP com todos os XMLs autorizados do período.";
    public string          PermissaoRequerida  => "relatorios.fiscal.xml-bulk";
    public string          SemanticVersion     => "1.0";
    public string          IconKey             => "archive";
    public int             MaxTentativas       => 3;
    public long?           EstimatedMaxRows    => null;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao            => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados => [ReportFormat.Zip];

    public Type ParamsType => typeof(XmlBulkDownloadParams);
    public Type RowType    => typeof(XmlBulkDownloadRow);
}
