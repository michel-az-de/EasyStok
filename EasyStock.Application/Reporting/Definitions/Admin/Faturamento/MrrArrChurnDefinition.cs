using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Admin.Faturamento;

/// <summary>Metadados do relatório MRR/ARR/Churn — Admin SaaS.</summary>
public sealed class MrrArrChurnDefinition : IReportDefinition
{
    public string Key => "admin.faturamento.mrr-arr-churn";
    public ReportCategoria Categoria => ReportCategoria.AdminSaaS;
    public ReportContexto Contexto => ReportContexto.AdminSaaS;
    public string Label => "MRR / ARR / Churn";
    public string Descricao => "Métricas mensais de recorrência: assinaturas ativas, receita realizada, churn rate e ARR.";
    public string PermissaoRequerida => "admin.relatorios.faturamento.consultar";
    public string SemanticVersion => "1.0";
    public string IconKey => "trending-up";
    public int MaxTentativas => 3;
    public long? EstimatedMaxRows => 120; // até 10 anos de dados
    public bool AvailableForTriggers => false;
    public TimeSpan Retencao => TimeSpan.FromDays(90);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(MrrArrChurnParams);
    public Type RowType => typeof(MrrArrChurnRow);
}
