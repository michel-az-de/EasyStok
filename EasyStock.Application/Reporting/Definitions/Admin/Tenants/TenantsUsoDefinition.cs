using EasyStock.Domain.Reporting;

namespace EasyStock.Application.Reporting.Definitions.Admin.Tenants;

/// <summary>Metadados do relatório de uso de tenants — Admin SaaS.</summary>
public sealed class TenantsUsoDefinition : IReportDefinition
{
    public string          Key                  => "admin.tenants.uso";
    public ReportCategoria Categoria            => ReportCategoria.AdminSaaS;
    public ReportContexto  Contexto             => ReportContexto.AdminSaaS;
    public string          Label                => "Uso de tenants";
    public string          Descricao            => "Visão geral de todas as empresas: plano, status, logins, usuários e receita.";
    public string          PermissaoRequerida   => "admin.relatorios.tenants.consultar";
    public string          SemanticVersion      => "1.0";
    public string          IconKey              => "building-2";
    public int             MaxTentativas        => 3;
    public long?           EstimatedMaxRows     => 10000;
    public bool            AvailableForTriggers => false;
    public TimeSpan        Retencao             => TimeSpan.FromDays(90);

    public IReadOnlyList<ReportFormat> FormatosSuportados =>
        [ReportFormat.Csv, ReportFormat.Xlsx];

    public Type ParamsType => typeof(TenantsUsoParams);
    public Type RowType    => typeof(TenantsUsoRow);
}
