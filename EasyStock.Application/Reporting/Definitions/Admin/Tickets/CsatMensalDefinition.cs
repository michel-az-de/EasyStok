using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Admin.Tickets;

/// <summary>Metadados do relatório de CSAT mensal — Admin SaaS.</summary>
public sealed class CsatMensalDefinition : IReportDefinition
{
    public string          Key                  => "admin.tickets.csat-mensal";
    public ReportCategoria Categoria            => ReportCategoria.AdminSaaS;
    public ReportContexto  Contexto             => ReportContexto.AdminSaaS;
    public string          Label                => "CSAT mensal";
    public string          Descricao            => "Avaliações de satisfação dos tickets no período, com média de nota e taxa de resposta.";
    public string          PermissaoRequerida   => "admin.relatorios.tickets.consultar";
    public string          SemanticVersion      => "1.0";
    public string          IconKey              => "star";
    public int             MaxTentativas        => 3;
    public long?           EstimatedMaxRows     => 5000;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao             => TimeSpan.FromDays(90);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(CsatMensalParams);
    public Type RowType    => typeof(CsatMensalRow);
}
