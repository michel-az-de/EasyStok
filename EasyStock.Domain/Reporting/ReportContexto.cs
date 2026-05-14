namespace EasyStock.Domain.Reporting;

/// <summary>
/// Identifica se o relatório é gerado no contexto de um Tenant
/// ou no contexto do Admin SaaS (cross-tenant).
/// </summary>
public enum ReportContexto : short
{
    Tenant = 1,
    AdminSaaS = 2,
}
