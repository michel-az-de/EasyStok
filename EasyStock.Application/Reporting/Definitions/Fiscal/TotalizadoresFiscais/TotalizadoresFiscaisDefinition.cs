using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Fiscal.TotalizadoresFiscais;

/// <summary>
/// Metadados estáticos do relatório "Totalizadores fiscais por CFOP/CST/NCM".
/// </summary>
public sealed class TotalizadoresFiscaisDefinition : IReportDefinition
{
    public string Key => "nfce.totalizadores";
    public ReportCategoria Categoria => ReportCategoria.Fiscal;
    public ReportContexto Contexto => ReportContexto.Tenant;
    public string Label => "Totalizadores fiscais por CFOP/CST/NCM";
    public string Descricao => "Resumo de NFC-e por CFOP, CST e NCM, com valores e impostos somados.";
    public string PermissaoRequerida => "relatorios.fiscal.consultar";
    public string SemanticVersion => "1.0";
    public string IconKey => "bar-chart";
    public int MaxTentativas => 3;
    public long? EstimatedMaxRows => null;
    public bool AvailableForTriggers => false;
    public TimeSpan Retencao => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(TotalizadoresFiscaisParams);
    public Type RowType => typeof(TotalizadoresFiscaisRow);
}
