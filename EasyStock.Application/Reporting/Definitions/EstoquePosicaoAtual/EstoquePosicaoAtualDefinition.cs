using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.EstoquePosicaoAtual;

/// <summary>
/// Metadados estáticos do relatório "Posição de estoque".
/// Nota: CustoUnitario = custo da última entrada, não CMA.
/// </summary>
public sealed class EstoquePosicaoAtualDefinition : IReportDefinition
{
    public string          Key                  => "estoque.posicao-atual";
    public ReportCategoria Categoria            => ReportCategoria.Estoque;
    public ReportContexto  Contexto             => ReportContexto.Tenant;
    public string          Label                => "Posição de estoque";
    public string          Descricao            => "Quantidade atual e custo da última entrada por produto.";
    public string          PermissaoRequerida   => "relatorios.estoque.consultar";
    public string          SemanticVersion      => "1.0";
    public string          IconKey              => "package";
    public int             MaxTentativas        => 3;
    public long?           EstimatedMaxRows     => 50_000;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao             => TimeSpan.FromDays(30);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(EstoquePosicaoAtualParams);
    public Type RowType    => typeof(EstoquePosicaoAtualRow);
}
