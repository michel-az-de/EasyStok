using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Fiscal.CancelamentosInutilizacoes;

/// <summary>
/// Metadados estáticos do relatório "Cancelamentos e inutilizações".
/// </summary>
public sealed class CancelamentosInutilizacoesDefinition : IReportDefinition
{
    public string          Key                 => "nfce.cancelamentos-inutilizacoes";
    public ReportCategoria Categoria           => ReportCategoria.Fiscal;
    public ReportContexto  Contexto            => ReportContexto.Tenant;
    public string          Label               => "Cancelamentos e inutilizações";
    public string          Descricao           => "Histórico de NFC-e canceladas e numerações inutilizadas.";
    public string          PermissaoRequerida  => "relatorios.fiscal.consultar";
    public string          SemanticVersion     => "1.0";
    public string          IconKey             => "x-circle";
    public int             MaxTentativas       => 3;
    public long?           EstimatedMaxRows    => null;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao            => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(CancelamentosInutilizacoesParams);
    public Type RowType    => typeof(CancelamentosInutilizacoesRow);
}
