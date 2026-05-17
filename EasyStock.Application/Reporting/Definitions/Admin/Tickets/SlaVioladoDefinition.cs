using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Admin.Tickets;

/// <summary>Metadados do relatório de SLA violado — Admin SaaS.</summary>
public sealed class SlaVioladoDefinition : IReportDefinition
{
    public string          Key                  => "admin.tickets.sla-violado";
    public ReportCategoria Categoria            => ReportCategoria.AdminSaaS;
    public ReportContexto  Contexto             => ReportContexto.AdminSaaS;
    public string          Label                => "Tickets com SLA violado";
    public string          Descricao            => "Tickets onde o prazo de resposta ou resolução foi excedido no período.";
    public string          PermissaoRequerida   => "admin.relatorios.tickets.consultar";
    public string          SemanticVersion      => "1.0";
    public string          IconKey              => "clock";
    public int             MaxTentativas        => 3;
    public long?           EstimatedMaxRows     => 2000;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao             => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(SlaVioladoParams);
    public Type RowType    => typeof(SlaVioladoRow);
}
