using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Fiscal.MapMensal;

/// <summary>
/// Metadados estáticos do relatório "MAP — Mapa Resumo NFC-e".
/// Fase 2. PDF formalmente imprimível será adicionado em Fase 2b (QuestPDF).
/// </summary>
public sealed class MapMensalDefinition : IReportDefinition
{
    public string Key => "nfce.map-mensal";
    public ReportCategoria Categoria => ReportCategoria.Fiscal;
    public ReportContexto Contexto => ReportContexto.Tenant;
    public string Label => "MAP — Mapa Resumo NFC-e";
    public string Descricao => "Mapa Resumo mensal de NFC-e em formato de tabela, um dia por linha.";
    public string PermissaoRequerida => "relatorios.fiscal.consultar";
    public string SemanticVersion => "1.0";
    public string IconKey => "calendar";
    public int MaxTentativas => 3;
    public long? EstimatedMaxRows => 31;   // 31 dias max por mês
    public bool AvailableForTriggers => false;
    public TimeSpan Retencao => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(MapMensalParams);
    public Type RowType => typeof(MapMensalRow);
}
