using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Fiscal.LivroSaidas;

/// <summary>
/// Metadados estáticos do relatório "Livro de Saídas (NFC-e)".
/// Fase 2 — requer migration PR-D (AddNfceTaxFields).
/// </summary>
public sealed class LivroSaidasDefinition : IReportDefinition
{
    public string          Key                 => "nfce.livro-saidas";
    public ReportCategoria Categoria           => ReportCategoria.Fiscal;
    public ReportContexto  Contexto            => ReportContexto.Tenant;
    public string          Label               => "Livro de Saídas (NFC-e)";
    public string          Descricao           => "Todas as NFC-e autorizadas e canceladas no período, organizadas para o livro fiscal.";
    public string          PermissaoRequerida  => "relatorios.fiscal.consultar";
    public string          SemanticVersion     => "1.0";
    public string          IconKey             => "file-text";
    public int             MaxTentativas       => 3;
    public long?           EstimatedMaxRows    => null;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao            => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(LivroSaidasParams);
    public Type RowType    => typeof(LivroSaidasRow);
}
