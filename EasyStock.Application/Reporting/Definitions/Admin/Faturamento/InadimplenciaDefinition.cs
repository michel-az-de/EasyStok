using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Admin.Faturamento;

/// <summary>Metadados do relatório de inadimplência — Admin SaaS.</summary>
public sealed class InadimplenciaDefinition : IReportDefinition
{
    public string          Key                  => "admin.faturamento.inadimplencia";
    public ReportCategoria Categoria            => ReportCategoria.AdminSaaS;
    public ReportContexto  Contexto             => ReportContexto.AdminSaaS;
    public string          Label                => "Inadimplência";
    public string          Descricao            => "Faturas vencidas com saldo devedor, ordenadas por dias de atraso.";
    public string          PermissaoRequerida   => "admin.relatorios.faturamento.consultar";
    public string          SemanticVersion      => "1.0";
    public string          IconKey              => "alert-triangle";
    public int             MaxTentativas        => 3;
    public long?           EstimatedMaxRows     => 5000;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao             => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(InadimplenciaParams);
    public Type RowType    => typeof(InadimplenciaRow);
}
