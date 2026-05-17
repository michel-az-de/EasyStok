using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.VendasPorPeriodo;

/// <summary>
/// Metadados estáticos do relatório "Vendas por período".
/// </summary>
public sealed class VendasPorPeriodoDefinition : IReportDefinition
{
    public string          Key                 => "vendas.por-periodo";
    public ReportCategoria Categoria           => ReportCategoria.Vendas;
    public ReportContexto  Contexto            => ReportContexto.Tenant;
    public string          Label               => "Vendas por período";
    public string          Descricao           => "Todas as vendas finalizadas no intervalo escolhido, com totais e formas de pagamento.";
    public string          PermissaoRequerida  => "relatorios.vendas.consultar";
    public string          SemanticVersion     => "1.2";
    public string          IconKey             => "bar-chart-2";
    public int             MaxTentativas       => 3;
    public long?           EstimatedMaxRows    => null;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao            => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(VendasPorPeriodoParams);
    public Type RowType    => typeof(VendasPorPeriodoRow);
}
